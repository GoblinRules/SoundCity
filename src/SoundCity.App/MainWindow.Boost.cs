using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SoundCity.Core;

namespace SoundCity.App;

public partial class MainWindow
{
    private void EqualizerClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var choice = CategoryPicker.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == (settings.Any(s => s.Type == "equalizer") ? "equalizer" : "windowsEqualizer"));
        if (choice is null) { Status.Text = "Connect a supported headphone and load its controls to edit EQ."; return; }
        SearchBox.Text = ""; CategoryPicker.SelectedItem = choice; RenderSettings();
    }

    private void BoostClick(object sender, RoutedEventArgs e) { if (!busy) ShowVolumeBoost(); }

    private void ShowVolumeBoost()
    {
        if (Selected is not { } device) return;
        var category = CategoryPicker.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id == "windowsBoost");
        if (category is not null && (CategoryPicker.SelectedItem as CategoryChoice)?.Id != category.Id) { CategoryPicker.SelectedItem = category; return; }
        ControlsPanel.Children.Clear(); AudioPanel.Visibility = Visibility.Collapsed;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "A little more volume", FontSize = 23, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Extra gain for quiet recordings, applied to this Windows headphone output.", Foreground = Theme("Muted"), FontSize = 12, Margin = new(0, 8, 0, 16) });
        var config = demo ? null : EqualizerApo.FindConfigDirectory();
        var status = new TextBlock { Text = config is null ? "SETUP REQUIRED · EQUALIZER APO" : "EQUALIZER APO FOUND", Foreground = Theme("Accent"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) };
        content.Children.Add(status);
        var instructions = config is null
            ? "Install the free Equalizer APO audio component, select your headphone playback device in its setup, and restart Windows if requested. Then return here and press Check setup."
            : "Enable Equalizer APO for your headphone output in Device Selector / Configurator. Audio enhancements must be enabled for that device. Exclusive-mode playback can bypass processing.";
        var setupActions = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var get = new Button { Content = config is null ? "Get Equalizer APO" : "Setup guide", Margin = new(0, 0, 10, 0) };
        get.Click += (_, _) => Process.Start(new ProcessStartInfo(config is null ? "https://sourceforge.net/projects/equalizerapo/" : "https://sourceforge.net/p/equalizerapo/wiki/Documentation/") { UseShellExecute = true });
        setupActions.Children.Add(get);
        var setupPath = EqualizerApo.FindDeviceSetup();
        if (setupPath is not null)
        {
            var setup = new Button { Content = "Open device setup", Margin = new(0, 0, 10, 0) };
            setup.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true }); } catch (Exception ex) { status.Text = ex.Message; } };
            setupActions.Children.Add(setup);
        }
        var recheck = new Button { Content = "Check setup", Style = (Style)FindResource("Ghost") };
        recheck.Click += (_, _) => ShowVolumeBoost(); setupActions.Children.Add(recheck);
        try
        {
            var outputs = demo ? [] : WindowsAudio.ForHeadphone(WindowsAudio.ReadOutputs(), device, o => EqualizerApo.EndpointState(o.Id) == ApoEndpointState.Configured);
            var picker = new ComboBox { ItemsSource = outputs, DisplayMemberPath = "Name", SelectedIndex = outputs.Length > 0 ? 0 : -1, Margin = new(0, 0, 0, 16) };
            AutomationProperties.SetName(picker, "Volume boost headphone output"); content.Children.Add(picker);
            var enabled = new CheckBox { Content = "Enable volume boost", VerticalAlignment = VerticalAlignment.Center, IsEnabled = config is not null && outputs.Length > 0 };
            var mode = new GainViewSwitch(true, controlViews);
            var controlRow = new DockPanel { Margin = new(0, 0, 0, 15) };
            DockPanel.SetDock(mode, Dock.Right); controlRow.Children.Add(mode); controlRow.Children.Add(enabled); content.Children.Add(controlRow);
            var gainPanel = new Grid { Margin = new(0, 0, 0, 20) };
            gainPanel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            gainPanel.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            gainPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
            gainPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var dial = new RotaryDial { Width = 184, Height = 184, ShowValue = true, Minimum = 0, Maximum = 12, SmallChange = 0.5, LargeChange = 1, IsEnabled = enabled.IsEnabled, Margin = new(4, 0, 24, 0) };
            AutomationProperties.SetName(dial, "Volume boost dial in dB"); gainPanel.Children.Add(dial);
            var slider = new GainSlider { Style = (Style)FindResource("GainSliderStyle"), Minimum = 0, Maximum = 12, SmallChange = 0.5, LargeChange = 1, TickFrequency = 0.5, IsSnapToTickEnabled = true, Margin = new(4, 16, 4, 0) };
            AutomationProperties.SetName(slider, "Volume boost slider in dB");
            slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(RangeBase.Value)) { Source = dial, Mode = BindingMode.TwoWay });
            slider.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = dial });
            Grid.SetRow(slider, 1); Grid.SetColumnSpan(slider, 2); gainPanel.Children.Add(slider);
            var gainDetails = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            gainDetails.Children.Add(new TextBlock { Text = "EXTRA GAIN", Foreground = Theme("Accent"), FontSize = 10, FontWeight = FontWeights.SemiBold });
            var level = new TextBlock { Text = "+0.0 dB", FontSize = 32, FontWeight = FontWeights.SemiBold, Margin = new(0, 6, 0, 9) };
            gainDetails.Children.Add(level);
            gainDetails.Children.Add(new TextBlock { Text = "0 to +12 dB · half-decibel steps\nHover + mouse wheel, drag, or use arrow keys.", Foreground = Theme("Muted"), FontSize = 12, LineHeight = 21 });
            gainDetails.Children.Add(new TextBlock { Text = "Choose your level, then press Apply boost.", Foreground = Theme("Subtle"), FontSize = 11, Margin = new(0, 10, 0, 0) });
            Grid.SetColumn(gainDetails, 1); gainPanel.Children.Add(gainDetails);
            void ShowView(bool useSliders)
            {
                dial.Visibility = useSliders ? Visibility.Collapsed : Visibility.Visible;
                slider.Visibility = useSliders ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(gainDetails, useSliders ? 0 : 1);
                Grid.SetColumnSpan(gainDetails, useSliders ? 2 : 1);
            }
            mode.Changed += ShowView;
            ShowView(mode.UsesSliders);
            content.Children.Add(new Border { Child = gainPanel, Background = Theme("Input"), BorderBrush = Theme("Stroke"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(16, 16, 16, 0), Margin = new(0, 0, 0, 16) });
            dial.ValueChanged += (_, _) => level.Text = $"+{dial.Value:0.0} dB";
            content.Children.Add(new TextBlock { Text = "Start low. Positive gain can clip loud recordings; reduce it if you hear distortion. This boost adds to your headphone EQ and any existing Windows filters.", Foreground = Theme("Subtle"), FontSize = 12, LineHeight = 20, Margin = new(0, 0, 0, 15) });
            var apply = new Button { Content = "Apply boost", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = enabled.IsEnabled };
            content.Children.Add(apply);
            void Load()
            {
                if (config is null || picker.SelectedItem is not AudioOutput output) return;
                var state = EqualizerApo.EndpointState(output.Id);
                enabled.IsEnabled = dial.IsEnabled = apply.IsEnabled = state == ApoEndpointState.Configured;
                var saved = new EqualizerApo(config).Read().SingleOrDefault(s => s.Endpoint == EqualizerApo.EndpointGuid(output.Id));
                enabled.IsChecked = saved?.Enabled ?? false; dial.Value = saved?.GainDb ?? 0;
                status.Text = state != ApoEndpointState.Configured ? EqualizerApo.EndpointSetupMessage(state) : saved?.Enabled == true ? $"BOOST CONFIGURED · +{saved.GainDb:0.0} dB" : "BOOST OFF · OUTPUT SETUP CHECKED";
            }
            picker.SelectionChanged += (_, _) => { try { Load(); } catch (Exception ex) { status.Text = ex.Message; apply.IsEnabled = false; } };
            apply.Click += async (_, _) =>
            {
                if (config is null || Selected?.Address != device.Address || picker.SelectedItem is not AudioOutput output) return;
                await RunAsync("Saving Windows volume boost…", _ =>
                {
                    if (!WindowsAudio.ReadOutputs().Any(o => o.Id == output.Id && WindowsAudio.Matches(o.Name, device))) throw new InvalidOperationException("This headphone output is no longer connected.");
                    if (EqualizerApo.EndpointState(output.Id) != ApoEndpointState.Configured) throw new InvalidOperationException("Equalizer APO is no longer configured for this output. Check device setup.");
                    var apo = new EqualizerApo(config);
                    var endpoint = EqualizerApo.EndpointGuid(output.Id);
                    var previous = apo.Read().SingleOrDefault(s => s.Endpoint == endpoint) ?? new(endpoint, 0, false);
                    apo.Save(previous with { GainDb = dial.Value, Enabled = enabled.IsChecked == true });
                    Load();
                    Status.Text = "Boost configuration saved. It takes effect when Equalizer APO is enabled for this output.";
                    return Task.CompletedTask;
                });
            };
            Load();
            if (outputs.Length == 0) status.Text = demo ? "DEMO · BOOST CANNOT AFFECT AUDIO" : "Connect these headphones to choose their Windows output.";
        }
        catch (Exception ex) { status.Text = "Boost configuration unavailable: " + ex.Message; }
        content.Children.Add(new Border { Height = 1, Background = Theme("Stroke"), Margin = new(0, 20, 0, 16) });
        content.Children.Add(setupActions);
        content.Children.Add(new TextBlock { Text = instructions, Foreground = Theme("Subtle"), LineHeight = 20, FontSize = 11 });
        ControlsPanel.Children.Add(Card(content));
        ControlsScroll.ScrollToTop();
        Status.Text = config is null ? "Volume boost needs Equalizer APO setup. No audio settings have been changed." : "Boost is configured per headphone output. Choose the gain, then Apply.";
    }
}
