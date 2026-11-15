using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SoundCity.Core;

namespace SoundCity.App;

public partial class MainWindow
{
    private void ShowWindowsEqualizer()
    {
        if (Selected is not { } device) return;
        ControlsPanel.Children.Clear(); AudioPanel.Visibility = Visibility.Collapsed;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Windows equaliser", FontSize = 23, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Fine-tune this PC’s output. Your headphone’s onboard EQ stays separate.", Foreground = Theme("Muted"), FontSize = 12, LineHeight = 20, Margin = new(0, 5, 0, 12) });
        var config = demo ? null : EqualizerApo.FindConfigDirectory();
        var status = new TextBlock { Text = config is null ? "SETUP REQUIRED · EQUALIZER APO" : "EQUALIZER APO FOUND", Foreground = Theme("Accent"), FontSize = 10, Margin = new(0, 0, 0, 12) };
        content.Children.Add(status);
        try
        {
            var outputs = demo ? [] : WindowsAudio.ForHeadphone(WindowsAudio.ReadOutputs(), device, o => EqualizerApo.EndpointState(o.Id) == ApoEndpointState.Configured);
            var outputRow = new Grid { Margin = new(0, 0, 0, 20) };
            outputRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            outputRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var picker = new ComboBox { ItemsSource = outputs, DisplayMemberPath = "Name", SelectedIndex = outputs.Length > 0 ? 0 : -1 };
            AutomationProperties.SetName(picker, "Windows equaliser output"); outputRow.Children.Add(picker); content.Children.Add(outputRow);
            var editorHost = new StackPanel(); content.Children.Add(editorHost);
            void Load()
            {
                editorHost.Children.Clear();
                var output = picker.SelectedItem as AudioOutput;
                var endpoint = output is null ? Guid.Empty : EqualizerApo.EndpointGuid(output.Id);
                var saved = config is null || endpoint == Guid.Empty ? null : new EqualizerApo(config).Read().SingleOrDefault(s => s.Endpoint == endpoint);
                var state = output is null ? ApoEndpointState.NotInstalled : EqualizerApo.EndpointState(output.Id);
                var enabled = new CheckBox { Content = "Enable EQ", IsChecked = saved?.EqEnabled == true, Margin = new(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = config is not null && output is not null && state == ApoEndpointState.Configured };
                AutomationProperties.SetName(enabled, "Enable Windows EQ");
                if (outputRow.Children.Count > 1) outputRow.Children.RemoveAt(1);
                Grid.SetColumn(enabled, 1); outputRow.Children.Add(enabled);
                var apply = new Button { Style = (Style)FindResource("Primary"), IsEnabled = enabled.IsEnabled };
                var setting = new DeviceSetting("windowsEqualizer", "windowsEqualizer", "equalizer",
                    JsonSerializer.SerializeToElement(new { bandHz = EqualizerApo.EqBands, min = -120, max = 120, fractionDigits = 1 }), JsonSerializer.SerializeToElement(saved?.EqGains));
                var key = $"{device.Address}:windowsEqualizer:{endpoint}";
                var draft = eqDrafts.TryGetValue(key, out var previous) ? previous.Gains : null;
                var editor = new EqualizerEditor(setting, device.Profile.Id + "-windows", eqProfiles, draft, "31 Hz–16 kHz · 0.1 dB steps · headroom reserved for raised bands", apply, true, controlViews);
                editor.Edited += gains => eqDrafts[key] = (EqualizerApo.EqBands, gains);
                editorHost.Children.Add(editor);
                status.Text = config is null ? "SETUP REQUIRED · EQUALIZER APO" : output is null ? "CONNECT YOUR HEADPHONE OUTPUT" : state != ApoEndpointState.Configured ? EqualizerApo.EndpointSetupMessage(state) : saved?.EqEnabled == true ? "WINDOWS EQ CONFIGURED · OUTPUT SETUP CHECKED" : "WINDOWS EQ OFF · OUTPUT SETUP CHECKED";
                apply.Click += async (_, _) =>
                {
                    if (config is null || output is null || Selected?.Address != device.Address) return;
                    await RunAsync("Saving Windows EQ…", _ =>
                    {
                        if (!WindowsAudio.ReadOutputs().Any(o => o.Id == output.Id && WindowsAudio.Matches(o.Name, device))) throw new InvalidOperationException("This headphone output is no longer connected.");
                        if (EqualizerApo.EndpointState(output.Id) != ApoEndpointState.Configured) throw new InvalidOperationException("Equalizer APO is no longer configured for this output. Check device setup.");
                        var apo = new EqualizerApo(config);
                        var prior = apo.Read().SingleOrDefault(s => s.Endpoint == endpoint) ?? new(endpoint, 0, false);
                        apo.Save(prior with { EqGains = editor.Gains, EqEnabled = enabled.IsChecked == true });
                        eqDrafts.Remove(key);
                        Load();
                        Status.Text = "Windows EQ configuration saved. Processing depends on Equalizer APO being enabled for this output.";
                        return Task.CompletedTask;
                    });
                };
            }
            picker.SelectionChanged += (_, _) => { try { Load(); } catch (Exception ex) { editorHost.Children.Clear(); status.Text = ex.Message; } };
            Load();
        }
        catch (Exception ex) { status.Text = "Windows EQ is unavailable: " + ex.Message; }
        var setup = new Button { Content = "Audio processing setup", Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        setup.Click += (_, _) => ShowVolumeBoost(); content.Children.Add(setup);
        content.Children.Add(new TextBlock { Text = config is null ? "You can design and save a curve now. Apply becomes available after Equalizer APO is set up for a connected output." : "Curves are personal tuning, not measured AutoEq corrections. Exclusive-mode audio can bypass Windows processing.", Foreground = Theme("Subtle"), FontSize = 11, LineHeight = 18, Margin = new(0, 6, 0, 0) });
        ControlsPanel.Children.Add(Card(content)); ControlsScroll.ScrollToTop();
    }
}
