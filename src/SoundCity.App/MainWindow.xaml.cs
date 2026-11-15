using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SoundCity.Core;

namespace SoundCity.App;

public partial class MainWindow : Window
{
    private readonly DeviceProfileMappings modelMappings = new(Path.Combine(AppPaths.DataDirectory, "device-models.json"));
    private readonly BluetoothDiscovery discovery;
    private readonly SoundcoreBackend backend;
    private readonly UgreenBackend ugreen = new();
    private readonly ParrotBackend parrot = new();
    private readonly OpenScq30Runner runner;
    private readonly bool demo;
    private readonly ControlViewPreferences controlViews;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private CancellationTokenSource? operation;
    private IReadOnlyList<DeviceSetting> settings = [];
    private ServiceReport? serviceReport;
    private bool busy;
    private DateTimeOffset? readAt;
    private readonly EqualizerProfiles eqProfiles = new(Path.Combine(AppPaths.DataDirectory, "eq-profiles"));
    private readonly Dictionary<string, (int[] Frequencies, int[] Gains)> eqDrafts = [];
    private Headphone? Selected => DevicesList.SelectedItem as Headphone;
    private record CategoryChoice(string Id, string Name) { public override string ToString() => Name; }
    private record OptionChoice(string Id, string Name) { public override string ToString() => Name; }

    public MainWindow(bool demo, bool openWindowsEqualizer = false, ControlViewPreferences? controlViews = null)
    {
        this.controlViews = controlViews ?? ControlViewPreferences.Current;
        InitializeComponent();
        this.demo = demo;
        discovery = new(modelMappings);
        var database = demo ? Path.Combine(AppPaths.DataDirectory, "demo.sqlite") : AppPaths.Database;
        runner = new OpenScq30Runner(AppPaths.Backend, database);
        backend = new SoundcoreBackend(runner);
        DemoBanner.Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
        if (demo) Title = "SoundCity — Demo";
        using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SoundCity.ico")).Stream)
        using (var sourceIcon = new System.Drawing.Icon(resource, new System.Drawing.Size(32, 32)))
            trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        tray = new System.Windows.Forms.NotifyIcon { Text = "SoundCity", Icon = trayIcon };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            Renderer = new DarkTrayRenderer(), ShowImageMargin = false,
            BackColor = DarkTrayRenderer.Surface, ForeColor = DarkTrayRenderer.Ink
        };
        menu.Items.Add("Open SoundCity", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("Refresh devices", null, (_, _) => Dispatcher.Invoke(() => ScanClick(this, new RoutedEventArgs())));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        Closed += (_, _) => { operation?.Cancel(); tray.Dispose(); menu.Dispose(); trayIcon.Dispose(); };
        Loaded += async (_, _) =>
        {
            await RunAsync("Finding your paired headphones…", DiscoverAsync);
            if (openWindowsEqualizer && Selected is not null)
                CategoryPicker.SelectedItem = CategoryPicker.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == "windowsEqualizer");
        };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.R) { ReadClick(this, new RoutedEventArgs()); e.Handled = true; }
            if (e.Key == Key.D) { ScanClick(this, new RoutedEventArgs()); e.Handled = true; }
        };
    }

    private async Task DiscoverAsync(CancellationToken token)
    {
        var previous = Selected?.Address;
        if (demo)
        {
            await runner.RunAsync(["paired-devices", "add", "--mac-address", "00:00:00:00:00:01", "--model", "SoundcoreA3954", "--demo"], token);
            DevicesList.ItemsSource = new[] { new Headphone("Demo Liberty 4 Pro", 1, "demo", false, DeviceCatalog.Liberty4Pro) };
        }
        else DevicesList.ItemsSource = await discovery.FindAsync(token);
        Headphone? playing = null;
        if (!demo) { try { playing = WindowsAudio.DefaultHeadphone(DevicesList.Items.Cast<Headphone>(), WindowsAudio.ReadOutputs()); } catch { /* Device discovery remains usable if Core Audio is unavailable. */ } }
        DevicesList.SelectedItem = DevicesList.Items.Cast<Headphone>().FirstOrDefault(d => d.Address == previous) ?? playing ?? DevicesList.Items.Cast<Headphone>().FirstOrDefault();
        if (Selected is null) ShowMessage("No matching headphones found", "Pair your headphones in Windows Bluetooth settings, then refresh devices. SoundCity recognizes Liberty 4 Pro, Life P3, UGREEN FitBuds, Studio Max5s, Parrot Zik 3, Beats Solo Buds and AirPods Pro 3. Use Identify headphones for a personal or shortened Bluetooth name.");
        Status.Text = $"Found {DevicesList.Items.Count} paired device(s).";
        if (Selected is { } selected) await AutoLoadAsync(selected, token);
    }

    private async void DeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        settings = [];
        readAt = null;
        serviceReport = null;
        FilterBar.Visibility = Visibility.Collapsed;
        BatterySummary.Text = "";
        BatterySummary.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        Status.Text = "Ready";
        Status.Foreground = Theme("Muted");
        P3Confirmation.IsChecked = false;
        ReadButton.Content = "Refresh controls";
        var device = Selected;
        P3Confirmation.Visibility = device?.Profile.NeedsConfirmation == true ? Visibility.Visible : Visibility.Collapsed;
        if (device is null) { AudioPanel.Children.Clear(); ControlsPanel.Children.Clear(); SetBusy(busy); return; }
        DeviceTitle.Text = device.Profile.Name;
        DeviceSubtitle.Text = demo ? "Simulated device · no real headphones are controlled" : $"{device.Profile.Brand} · {device.Profile.ModelCode} · {device.ConnectionLabel}";
        if (!demo && device.Profile == DeviceCatalog.Zik3) DeviceSubtitle.Text += " · Experimental controls";
        ConnectionBadge.Text = demo ? "DEMO SESSION" : device.Connected ? "CONNECTED TO WINDOWS" : "PAIRED DEVICE";
        RenderWindowsAudio();
        if (device.Profile.HasControls)
            ShowMessage(device.Profile.NeedsConfirmation ? "Confirm your model to continue" : "Your headphone controls", device.Profile.NeedsConfirmation ? "Check that your P3 is model A3939, then enable the confirmation above. This selects the correct headphone driver." : device.Connected || demo ? "Reading your listening modes, equalizer and device preferences. Changes take effect when you press Apply." : "Turn on your headphones and connect them to Windows, then refresh devices to load listening modes, EQ and other supported settings.");
        else
        {
            CategoryPicker.ItemsSource = new[] { new CategoryChoice("all", "Support & audio"), new CategoryChoice("windowsEqualizer", "Windows equaliser"), new CategoryChoice("windowsBoost", "Volume boost") };
            CategoryPicker.SelectedIndex = 0;
            FilterBar.Visibility = Visibility.Visible;
            ShowPendingControls(device);
        }
        SetBusy(busy);
        if (!busy && device.Profile.HasControls && (device.Connected || demo) && !device.Profile.NeedsConfirmation)
            await RunAsync("Reading your controls…", token => AutoLoadAsync(device, token));
    }

    private async void ScanClick(object sender, RoutedEventArgs e) => await RunAsync("Finding your paired headphones…", DiscoverAsync);
    private async void ReadClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } device) return;
        var confirmed = P3Confirmation.IsChecked == true;
        await RunAsync("Reading headphone settings…", async token =>
        {
            RenderWindowsAudio();
            if (!device.Profile.HasControls)
            {
                RenderSettings();
                Status.Text = "Windows audio controls refreshed. See the support details for this model.";
                return;
            }
            if (!demo && device.Profile.BackendModel is not null) await backend.RegisterAsync(device, confirmed, token);
            await ReadSettingsAsync(device, token);
            Status.Text = $"Read {settings.Count} controls at {DateTime.Now:t}. No settings were changed.";
        });
    }

    private async Task AutoLoadAsync(Headphone device, CancellationToken token)
    {
        if (!device.Profile.HasControls || (!device.Connected && !demo) || (device.Profile.NeedsConfirmation && P3Confirmation.IsChecked != true)) return;
        Status.Text = "Reading headphone settings…";
        if (!demo && device.Profile.BackendModel is not null) await backend.RegisterAsync(device, P3Confirmation.IsChecked == true, token);
        await ReadSettingsAsync(device, token);
        Status.Text = $"{settings.Count} controls ready · Settings change only when you press Apply.";
    }

    private async void ConfirmationChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Selected is not { } device || !device.Profile.NeedsConfirmation) return;
        if (P3Confirmation.IsChecked == true) await RunAsync("Reading headphone settings…", token => AutoLoadAsync(device, token));
        else
        {
            settings = []; readAt = null; FilterBar.Visibility = Visibility.Collapsed;
            BatterySummary.Visibility = Visibility.Collapsed;
            ShowMessage("Confirm your model to continue", "Confirm A3939 above before loading the Life P3 controls.");
        }
        SetBusy(busy);
    }

    private async Task ReadSettingsAsync(Headphone device, CancellationToken token)
    {
        settings = device.Profile.Backend switch
        {
            HeadphoneBackend.Ugreen => await ugreen.ReadAsync(device, token),
            HeadphoneBackend.Parrot => await parrot.ReadAsync(device, token),
            HeadphoneBackend.Soundcore => await backend.ReadAsync(device.Address, token),
            _ => throw new InvalidOperationException("This model uses Windows audio controls only.")
        };
        readAt = DateTimeOffset.Now;
        BatterySummary.Text = string.Join("     ·     ", settings.Where(s => s.Id.Contains("BatteryLevel", StringComparison.OrdinalIgnoreCase)).Select(s => $"{s.Label}: {BatteryText(s.CurrentText)}"));
        BatterySummary.Visibility = string.IsNullOrEmpty(BatterySummary.Text) ? Visibility.Collapsed : Visibility.Visible;
        var prior = (CategoryPicker.SelectedItem as CategoryChoice)?.Id;
        var categories = new[] { new CategoryChoice("all", "All controls") }.Concat(settings.Select(s => s.Category).Distinct().Select(c => new CategoryChoice(c, Labels.Humanize(c)))).Concat(new[] { new CategoryChoice("windowsEqualizer", "Windows equaliser"), new CategoryChoice("windowsBoost", "Volume boost") }).ToArray();
        CategoryPicker.ItemsSource = categories;
        CategoryPicker.SelectedItem = categories.FirstOrDefault(c => c.Id == prior) ?? categories.FirstOrDefault(c => c.Id == (device.Profile == DeviceCatalog.Zik3 ? "soundModes" : "equalizer")) ?? categories[0];
        FilterBar.Visibility = Visibility.Visible;
        RenderSettings();
        ReadButton.Content = "Refresh controls";
    }

    private static string BatteryText(string value)
    {
        var pieces = value.Split('/');
        return pieces.Length == 2 && double.TryParse(pieces[0], out var level) && double.TryParse(pieces[1], out var maximum) && maximum > 0
            ? $"{Math.Round(level / maximum * 100)}%" : value;
    }

    private void CategoryChanged(object sender, SelectionChangedEventArgs e) => RenderSettings();
    private void SearchChanged(object sender, TextChangedEventArgs e) => RenderSettings();
    private void RenderSettings()
    {
        if (Selected is null) return;
        ControlsPanel.Children.Clear();
        var selected = (CategoryPicker.SelectedItem as CategoryChoice)?.Id ?? "all";
        if (selected == "windowsBoost") { ShowVolumeBoost(); return; }
        if (selected == "windowsEqualizer") { ShowWindowsEqualizer(); return; }
        if (settings.Count == 0) { if (!Selected.Profile.HasControls) ShowPendingControls(Selected); return; }
        AudioPanel.Visibility = selected == "equalizer" ? Visibility.Collapsed : Visibility.Visible;
        var query = SearchBox.Text.Trim();
        if (Selected.Profile == DeviceCatalog.Zik3 && selected is "all" or "equalizer")
        {
            var note = new TextBlock { Text = "Zik 3 support is experimental until checked on your headphones. Its saved onboard EQ can be switched on/off; custom onboard bands are not yet supported. Use Windows equaliser for ten-band tuning on this PC.", Foreground = Theme("Muted"), LineHeight = 22, Margin = new(0, 0, 0, 16) };
            ControlsPanel.Children.Add(note);
        }
        foreach (var category in settings.Where(s => (selected == "all" || s.Category == selected) && (query.Length == 0 || s.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || Labels.Humanize(s.Category).Contains(query, StringComparison.OrdinalIgnoreCase))).GroupBy(s => s.Category))
        {
            ControlsPanel.Children.Add(new TextBlock { Text = Labels.Humanize(category.Key), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 6, 0, 12) });
            foreach (var setting in category) ControlsPanel.Children.Add(CreateSettingCard(setting));
        }
        if (ControlsPanel.Children.Count == 0) ShowMessage("No matching controls", "Try a different search or choose All controls.");
        ControlsScroll.ScrollToTop();
    }

    private Border CreateSettingCard(DeviceSetting setting)
    {
        var content = new StackPanel();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(275) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = new TextBlock { Text = setting.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 15, 0), FontWeight = FontWeights.SemiBold };
        grid.Children.Add(label);
        if (setting.Type == "equalizer") return CreateEqualizer(setting);
        if (!setting.Editable)
        {
            var text = new TextBlock { Text = setting.CurrentText, Foreground = Theme("Muted"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1); Grid.SetColumnSpan(text, 2); grid.Children.Add(text);
            content.Children.Add(grid);
            return Card(content);
        }

        Func<string> getValue;
        FrameworkElement editor;
        switch (setting.Type)
        {
            case "toggle":
                var check = new CheckBox { Content = "Enabled", IsChecked = setting.Value.ValueKind == JsonValueKind.True, VerticalAlignment = VerticalAlignment.Center };
                editor = check; getValue = () => check.IsChecked == true ? "true" : "false"; break;
            case "select":
            case "optionalSelect":
            case "modifiableSelect":
            case "multiSelectWithRemove":
                var options = setting.Options.Select((id, i) => new OptionChoice(id, i < setting.LocalizedOptions.Length ? setting.LocalizedOptions[i] : Labels.Humanize(id))).ToList();
                if (setting.Type == "optionalSelect") options.Insert(0, new("", "None"));
                var combo = new ComboBox { ItemsSource = options, MinWidth = 180 };
                combo.SelectedItem = options.FirstOrDefault(o => o.Id == setting.CurrentText);
                editor = combo;
                getValue = () =>
                {
                    var choice = combo.SelectedItem as OptionChoice ?? throw new ArgumentException("Choose an option first.");
                    return setting.Type is "modifiableSelect" or "multiSelectWithRemove" ? "\\" + choice.Id : choice.Id;
                };
                break;
            case "multiSelect":
                var boxes = setting.Options.Select(option => new CheckBox { Content = option, IsChecked = setting.Value.ValueKind == JsonValueKind.Array && setting.Value.EnumerateArray().Any(v => v.GetString() == option) }).ToArray();
                var stack = new StackPanel(); foreach (var box in boxes) stack.Children.Add(box);
                editor = stack; getValue = () => string.Join(',', boxes.Where(b => b.IsChecked == true).Select(b => b.Content.ToString())); break;
            case "action":
                editor = new TextBlock { Text = "Run on your headphones", Foreground = Theme("Muted"), VerticalAlignment = VerticalAlignment.Center };
                getValue = () => ""; break;
            default:
                var value = setting.CurrentText;
                if (setting.Type == "timeOfDay" && int.TryParse(value, out var minutes)) value = $"{minutes / 60:00}:{minutes % 60:00}";
                var entry = new TextBox { Text = value };
                if (setting.Type == "i32Range") entry.ToolTip = $"{setting.Parameters.GetProperty("start")} to {setting.Parameters.GetProperty("end")}, step {setting.Parameters.GetProperty("step")}";
                editor = entry; getValue = () => entry.Text; break;
        }
        AutomationProperties.SetName(editor, setting.Label);
        editor.Margin = new(0, 0, 12, 0);
        Grid.SetColumn(editor, 1); grid.Children.Add(editor);
        var apply = ApplyButton(setting, getValue, setting.Type == "action" ? "Run" : "Apply");
        Grid.SetColumn(apply, 2); grid.Children.Add(apply);
        content.Children.Add(grid);

        var hint = setting.Id switch
        {
            "noiseCancellationLevel" => "Manual strength for noise cancelling mode.",
            "highQualityAudio" => "Headphone codec preference. Available playback codecs still depend on Windows.",
            "multipoint" => "Allow the headphones to connect to two devices.",
            "gameMode" => "Reduce playback latency. Connection stability may vary.",
            _ => null
        };
        if (hint is not null) content.Children.Add(new TextBlock { Text = hint, Foreground = Theme("Subtle"), FontSize = 11, Margin = new(0, 12, 0, 0) });

        if (setting.Type == "modifiableSelect")
        {
            var row = new DockPanel { Margin = new(0, 12, 0, 0) };
            var name = new TextBox { MinWidth = 200, ToolTip = "Name for the current EQ settings", Margin = new(0, 0, 10, 0) };
            AutomationProperties.SetName(name, "New EQ profile name");
            var save = ApplyButton(setting, () => "+" + name.Text.Trim(), "Save current EQ as profile");
            DockPanel.SetDock(save, Dock.Right); row.Children.Add(save); row.Children.Add(name); content.Children.Add(row);
            var remove = ApplyButton(setting, () => "-" + getValue().TrimStart('\\'), "Delete selected profile");
            remove.HorizontalAlignment = HorizontalAlignment.Right; remove.Margin = new(0, 9, 0, 0); content.Children.Add(remove);
        }
        if (setting.Type == "multiSelectWithRemove")
        {
            var remove = ApplyButton(setting, () => "-" + getValue().TrimStart('\\'), "Remove selected connection");
            remove.HorizontalAlignment = HorizontalAlignment.Right; remove.Margin = new(0, 9, 0, 0); content.Children.Add(remove);
        }
        return Card(content);
    }

    private Border CreateEqualizer(DeviceSetting setting)
    {
        var key = $"{Selected?.Address}:{setting.Id}";
        var frequencies = setting.Parameters.GetProperty("bandHz").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var draft = eqDrafts.TryGetValue(key, out var saved) && saved.Frequencies.SequenceEqual(frequencies) ? saved.Gains : null;
        var preset = settings.FirstOrDefault(s => s.Id == "equalizerPreset");
        var index = preset is null ? -1 : Array.IndexOf(preset.Options, preset.CurrentText);
        var active = index >= 0 ? $"Current headphone preset: {preset!.LocalizedOptions[index]}. " : "";
        EqualizerEditor? editor = null;
        var apply = ApplyButton(setting, () => string.Join(',', editor!.Gains), "Apply EQ");
        editor = new EqualizerEditor(setting, Selected?.Profile.Id ?? "demo", eqProfiles, draft,
            active + "Adjust each band, or load a starting curve. Double-click a control to reset it.", apply, viewPreferences: controlViews);
        editor.Edited += gains => eqDrafts[key] = (frequencies, gains);
        return Card(editor);
    }

    private Button ApplyButton(DeviceSetting setting, Func<string> read, string text)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(button, $"{text}: {setting.Label}");
        button.Click += async (_, _) =>
        {
            if (Selected is not { } device) return;
            await RunAsync($"Applying {setting.Label.ToLowerInvariant()}…", async token =>
            {
                var input = setting.Validate(read());
                switch (device.Profile.Backend)
                {
                    case HeadphoneBackend.Ugreen: await ugreen.SetAsync(device, setting, input, token); break;
                    case HeadphoneBackend.Parrot: await parrot.SetAsync(device, setting, input, token); break;
                    case HeadphoneBackend.Soundcore: await backend.SetAsync(device.Address, setting, input, token); break;
                    default: throw new InvalidOperationException("No headphone command driver is enabled for this model.");
                }
                if (setting.Type == "equalizer") eqDrafts.Remove($"{device.Address}:{setting.Id}");
                // Preserve a clear outcome if the device reconnects before readback.
                try { await ReadSettingsAsync(device, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    settings = []; ControlsPanel.Children.Clear(); FilterBar.Visibility = Visibility.Collapsed;
                    throw new InvalidOperationException($"The change was sent, but the device could not be read again. Refresh controls to check its state. {ex.Message}");
                }
                Status.Text = $"{setting.Label} applied. Settings refreshed at {DateTime.Now:t}.";
            });
        };
        return button;
    }

    private static Brush Theme(string key) => (Brush)Application.Current.FindResource(key);
    private static Border Card(UIElement content) => new() { Child = content, Background = Theme("Surface"), CornerRadius = new(11), Padding = new(21), Margin = new(0, 0, 0, 13), BorderBrush = Theme("Stroke"), BorderThickness = new(1) };

    private void RenderWindowsAudio()
    {
        AudioPanel.Children.Clear();
        AudioPanel.Visibility = Visibility.Visible;
        if (demo || Selected is not { } device) return;
        try
        {
            var outputs = WindowsAudio.ForHeadphone(WindowsAudio.ReadOutputs(), device);
            if (outputs.Length == 0) return;
            var content = new StackPanel();
            var heading = new DockPanel();
            var badge = new TextBlock { Text = "WINDOWS AUDIO", Foreground = Theme("Accent"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge);
            heading.Children.Add(new TextBlock { Text = "Output volume", FontWeight = FontWeights.SemiBold, FontSize = 18 });
            content.Children.Add(heading);
            var picker = new ComboBox { ItemsSource = outputs, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new(0, 14, 0, 0), Visibility = outputs.Length > 1 ? Visibility.Visible : Visibility.Collapsed };
            AutomationProperties.SetName(picker, "Windows audio endpoint");
            content.Children.Add(picker);
            var endpoint = new TextBlock { Foreground = Theme("Muted"), FontSize = 11, Margin = new(0, 6, 0, 18) };
            content.Children.Add(endpoint);
            var row = new Grid();
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var slider = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true, Style = (Style)FindResource("VolumeSlider"), Margin = new(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, "Windows output volume");
            var level = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme("Accent") };
            var mute = new CheckBox { Content = "Mute", Margin = new(10, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            var apply = new Button { Content = "Apply", MinWidth = 68 };
            Grid.SetColumn(level, 1); Grid.SetColumn(mute, 2); Grid.SetColumn(apply, 3);
            row.Children.Add(slider); row.Children.Add(level); row.Children.Add(mute); row.Children.Add(apply);
            content.Children.Add(row);
            var boost = new Button { Content = "Volume boost", Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 10, 0, 0) };
            boost.Click += (_, _) => { if (!busy) ShowVolumeBoost(); };
            content.Children.Add(boost);
            content.Children.Add(new TextBlock { Text = "Adjust the Windows output, then press Apply. Headphone ANC and EQ are separate settings.", Foreground = Theme("Subtle"), FontSize = 11, Margin = new(0, 16, 0, 0) });
            void LoadOutput()
            {
                if (picker.SelectedItem is not AudioOutput output) return;
                slider.Value = output.Volume; mute.IsChecked = output.Muted;
                level.Text = $"{output.Volume:0}%";
                endpoint.Text = output.Name + (output.IsDefault ? " · Default output" : " · Available output");
            }
            slider.ValueChanged += (_, _) => level.Text = $"{slider.Value:0}%";
            picker.SelectionChanged += (_, _) => LoadOutput();
            apply.Click += async (_, _) =>
            {
                if (Selected?.Address != device.Address || picker.SelectedItem is not AudioOutput output) return;
                await RunAsync("Updating Windows audio…", _ =>
                {
                    // Validate that the same named, active endpoint still belongs to this selection.
                    var current = WindowsAudio.ReadOutputs().SingleOrDefault(o => o.Id == output.Id && WindowsAudio.Matches(o.Name, device));
                    if (current is null) throw new InvalidOperationException("This audio output is no longer available. Refresh devices and try again.");
                    WindowsAudio.Set(current.Id, slider.Value, mute.IsChecked == true);
                    RenderWindowsAudio();
                    Status.Text = "Windows output updated.";
                    return Task.CompletedTask;
                });
            };
            LoadOutput();
            AudioPanel.Children.Add(Card(content));
        }
        catch (Exception ex)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = "Windows audio is unavailable", FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = ex.Message, Foreground = Theme("Muted"), Margin = new(0, 8, 0, 0) });
            AudioPanel.Children.Add(Card(content));
        }
    }

    private void ShowPendingControls(Headphone device)
    {
        ControlsPanel.Children.Clear();
        AudioPanel.Visibility = Visibility.Visible;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "WINDOWS AUDIO SUPPORT", FontSize = 10, Foreground = Theme("Accent"), FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = device.Connected ? "Your listening controls" : "Ready when you connect", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new(0, 9, 0, 10) });
        var description = device.Profile.Id switch
        {
            "solobuds" => "Solo Buds support Windows volume and mute here. They do not have active noise cancellation or transparency. Battery and button preferences are not available in SoundCity yet. The Studio Buds AutoEq profile is for a different model and is not applied to Solo Buds.",
            "airpodspro3" => "AirPods Pro 3 support Windows volume and mute here. Noise cancellation, Adaptive Audio, ear detection and individual battery levels are not integrated yet; use your iPhone or the headphone controls. Advanced Windows controls require a separate Apple-protocol driver integration.",
            _ => $"SoundCity recognizes your {device.Profile.Name}. Windows volume and mute are available for its connected output. Onboard EQ, listening modes and button settings still need exact-model protocol support."
        };
        content.Children.Add(new TextBlock { Text = description, Foreground = Theme("Muted"), LineHeight = 23 });
        content.Children.Add(new TextBlock { Text = "Windows equaliser gives you ten-band tuning and local profiles. EQ and volume boost need Equalizer APO setup for this output; both start off.", Foreground = Theme("Muted"), LineHeight = 23, Margin = new(0, 14, 0, 16) });
        var actions = new WrapPanel();
        var equalizer = new Button { Content = "Open Windows equaliser", Style = (Style)FindResource("Primary"), Margin = new(0, 0, 10, 0) };
        equalizer.Click += (_, _) => { CategoryPicker.SelectedItem = CategoryPicker.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == "windowsEqualizer"); };
        var boost = new Button { Content = "Volume boost", Style = (Style)FindResource("Ghost") }; boost.Click += BoostClick;
        actions.Children.Add(equalizer); actions.Children.Add(boost); content.Children.Add(actions);
        ControlsPanel.Children.Add(Card(content));
        var sound = new Button { Content = "Open Windows sound settings  ↗", Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left };
        sound.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        ControlsPanel.Children.Add(sound);
    }

    private void ShowMessage(string title, string body)
    {
        ControlsPanel.Children.Clear();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 20, Margin = new(0, 0, 0, 12) });
        stack.Children.Add(new TextBlock { Text = body, Foreground = Theme("Muted"), LineHeight = 24 });
        ControlsPanel.Children.Add(Card(stack));
    }

    private async void InspectClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } device || demo) return;
        await RunAsync("Inspecting Bluetooth services…", async token =>
        {
            serviceReport = await discovery.InspectAsync(device, token);
            ShowMessage("Bluetooth connection details", $"{serviceReport.Connection} · Service discovery: {serviceReport.Result}\n\n" +
                (serviceReport.Services.Count == 0 ? "No serial control services were returned. Power on the headphones, then retry." : string.Join("\n\n", serviceReport.Services.Select(s => $"{s.Name}\nService: {s.Uuid}"))) + "\n\nThis check sent no headphone setting commands. Export diagnostics to save these details.");
            Status.Text = $"Found {serviceReport.Services.Count} RFCOMM service(s). No settings were changed.";
        });
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } device) return;
        var dialog = new SaveFileDialog { FileName = $"soundcity-{device.Profile.Id}-diagnostics.json", Filter = "JSON diagnostics|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = new { createdUtc = DateTimeOffset.UtcNow, device = device.Profile.Name, model = device.Profile.ModelCode, demo, lastRead = readAt, services = serviceReport,
                settings = settings.Where(s => !s.Id.Contains("serial", StringComparison.OrdinalIgnoreCase) && !s.Id.Contains("Devices", StringComparison.OrdinalIgnoreCase)).Select(s => new { s.Id, s.Category, s.Type, value = s.CurrentText }) };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Status.Text = "Diagnostics exported. Bluetooth addresses, serial numbers and paired-device names are omitted.";
        }
        catch (Exception ex) { Status.Text = "Could not save diagnostics: " + ex.Message; }
    }

    private async Task RunAsync(string message, Func<CancellationToken, Task> work)
    {
        if (busy) return;
        operation = new CancellationTokenSource();
        operation.CancelAfter(TimeSpan.FromSeconds(90));
        SetBusy(true);
        Status.Foreground = Theme("Muted");
        Status.Text = message;
        try { await work(operation.Token); }
        catch (OperationCanceledException) { Status.Text = "Operation stopped. Refresh controls to check the current device state."; }
        catch (Exception ex) { Status.Text = ex.Message; Status.Foreground = Theme("Error"); }
        finally { SetBusy(false); operation.Dispose(); operation = null; }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        IdentifyButton.IsEnabled = !value && !demo;
        DevicesList.IsEnabled = !value; ScanButton.IsEnabled = !value; ControlsScroll.IsEnabled = !value;
        P3Confirmation.IsEnabled = !value; CategoryPicker.IsEnabled = !value;
        ReadButton.IsEnabled = !value && Selected is { } device && (!device.Profile.NeedsConfirmation || P3Confirmation.IsChecked == true);
        InspectButton.IsEnabled = !value && Selected is not null && !demo;
        ExportButton.IsEnabled = !value && Selected is not null;
        Progress.Visibility = CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void BluetoothClick(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
    private void TrayClick(object sender, RoutedEventArgs e) { tray.Visible = true; Hide(); }
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); tray.Visible = false; }
    private void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeClick(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
