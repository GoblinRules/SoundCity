using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SoundCity.Core;

namespace SoundCity.App;

internal sealed class EqualizerEditor : StackPanel
{
    private readonly RotaryDial[] dials;
    private readonly TextBox[] entries;
    private readonly double[] original;
    private readonly double factor;
    private readonly double minimum, maximum;
    private readonly GainCurve graph;
    private readonly TextBlock feedback;
    private readonly bool hasDeviceCurve;
    private readonly bool windowsEqualizer;
    private bool updating;
    internal int[] Frequencies { get; }
    internal int[] Gains => dials.Select(s => (int)Math.Round(s.Value * factor)).ToArray();
    internal event Action<int[]>? Edited;
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    internal EqualizerEditor(DeviceSetting setting, string model, EqualizerProfiles profiles, int[]? draft, string deviceState, Button apply, bool windowsEqualizer = false, ControlViewPreferences? viewPreferences = null)
    {
        this.windowsEqualizer = windowsEqualizer;
        Frequencies = setting.Parameters.GetProperty("bandHz").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        factor = Math.Pow(10, setting.Parameters.GetProperty("fractionDigits").GetInt32());
        minimum = setting.Parameters.GetProperty("min").GetDouble() / factor;
        maximum = setting.Parameters.GetProperty("max").GetDouble() / factor;
        hasDeviceCurve = setting.Value.ValueKind == System.Text.Json.JsonValueKind.Array && setting.Value.GetArrayLength() == Frequencies.Length;
        original = hasDeviceCurve ? setting.Value.EnumerateArray().Select(v => v.GetDouble() / factor).ToArray() : new double[Frequencies.Length];
        var heading = new DockPanel();
        var badge = new TextBlock { Text = $"{Frequencies.Length} BANDS  ·  {minimum:0.#} TO +{maximum:0.#} dB", Foreground = Brush("Accent"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge);
        heading.Children.Add(new TextBlock { Text = "Shape your sound", FontSize = 21, FontWeight = FontWeights.SemiBold });
        Children.Add(heading);
        Children.Add(new TextBlock { Text = deviceState, Foreground = Brush("Muted"), FontSize = 12, Margin = new(0, 7, 0, 15) });
        var shapes = new WrapPanel { Margin = new(0, 0, 0, 14) };
        foreach (var name in new[] { "Flat", "Warm", "Bass lift", "Vocals", "Bright" })
        {
            var button = SmallButton(name);
            button.ToolTip = "Load a SoundCity starting curve; press Apply to use it.";
            button.Click += (_, _) => SetGains(EqualizerProfiles.Shape(name, Frequencies));
            shapes.Children.Add(button);
        }
        var mode = new GainViewSwitch(false, viewPreferences) { Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var toolbar = new DockPanel();
        DockPanel.SetDock(mode, Dock.Right); toolbar.Children.Add(mode); toolbar.Children.Add(shapes);
        Children.Add(toolbar);
        graph = new GainCurve(Frequencies, minimum, maximum) { Height = 115, Margin = new(0, 0, 0, 14), ToolTip = "Gain curve for the EQ bands. This is not a measured headphone response." };
        AutomationProperties.SetName(graph, "Equaliser gain curve");
        feedback = new TextBlock { Foreground = Brush("Accent"), FontSize = 11, Margin = new(0, 3, 0, 14) };
        var bands = new System.Windows.Controls.Primitives.UniformGrid { Columns = (int)Math.Ceiling(Frequencies.Length / 2d), Margin = new(-4, 0, -4, 4) };
        dials = new RotaryDial[Frequencies.Length];
        var sliders = new GainSlider[Frequencies.Length];
        var tiles = new Border[Frequencies.Length];
        entries = new TextBox[Frequencies.Length];
        updating = true;
        for (var i = 0; i < Frequencies.Length; i++)
        {
            var index = i;
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(new TextBlock { Text = FormatFrequency(Frequencies[i]), Foreground = Brush("Ink"), FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Margin = new(0, 0, 0, 4) });
            var entry = new TextBox { Text = "0", Width = 60, Padding = new(3, 4, 3, 4), TextAlignment = TextAlignment.Center, FontSize = 12, Foreground = Brush("Accent"), Margin = new(0, 3, 0, 0) };
            AutomationProperties.SetName(entry, $"{Frequencies[i]} Hz gain in dB");
            entries[i] = entry;
            var dial = new RotaryDial { Minimum = minimum, Maximum = maximum, SmallChange = 1 / factor, LargeChange = 1, Value = original[i] };
            AutomationProperties.SetName(dial, $"{Frequencies[i]} Hz equaliser dial");
            dials[i] = dial;
            dial.ValueChanged += (_, _) => Update();
            var slider = new GainSlider { Style = (Style)Application.Current.FindResource("GainSliderStyle"), Orientation = Orientation.Vertical, Height = 140, Minimum = minimum, Maximum = maximum, TickFrequency = 1 / factor, SmallChange = 1 / factor, LargeChange = 1, IsSnapToTickEnabled = true, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(slider, $"{Frequencies[i]} Hz equaliser slider");
            slider.SetBinding(RangeBase.ValueProperty, new Binding(nameof(RangeBase.Value)) { Source = dial, Mode = BindingMode.TwoWay });
            sliders[i] = slider;
            void Commit()
            {
                if ((double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) || double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && double.IsFinite(value) && value >= minimum && value <= maximum)
                    dials[index].Value = Math.Round(value * factor) / factor;
                else feedback.Text = $"Enter a gain from {minimum:0.#} to +{maximum:0.#} dB.";
                entry.Text = FormatGain(dials[index].Value);
            }
            entry.LostKeyboardFocus += (_, _) => Commit();
            entry.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            column.Children.Add(dial); column.Children.Add(slider); column.Children.Add(entry);
            tiles[i] = new Border { Child = column, Background = Brush("Input"), BorderBrush = Brush("Stroke"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(2, 10, 2, 10), Margin = new(4, 4, 4, 4) };
            bands.Children.Add(tiles[i]);
        }
        Children.Add(bands);
        var hint = new TextBlock { Foreground = Brush("Subtle"), FontSize = 11, Margin = new(0, 4, 0, 13) };
        Children.Add(hint);
        void ShowView(bool useSliders)
        {
            bands.Columns = useSliders ? Frequencies.Length : (int)Math.Ceiling(Frequencies.Length / 2d);
            for (var i = 0; i < dials.Length; i++)
            {
                dials[i].Visibility = useSliders ? Visibility.Collapsed : Visibility.Visible;
                sliders[i].Visibility = useSliders ? Visibility.Visible : Visibility.Collapsed;
                entries[i].Width = useSliders ? 44 : 60;
                tiles[i].Margin = useSliders ? new(2, 4, 2, 4) : new(4, 4, 4, 4);
            }
            hint.Text = useSliders ? "Hover + mouse wheel, or drag a slider · Double-click for 0 dB" : "Hover + mouse wheel, or drag · Shift-drag for fine tuning · Double-click for 0 dB";
        }
        mode.Changed += ShowView;
        ShowView(mode.UsesSliders);
        Children.Add(new Border { Child = graph, Background = Brush("Input"), CornerRadius = new(8), Padding = new(8, 4, 8, 0), Margin = new(0, 0, 0, 12) });
        Children.Add(feedback);
        var actions = new DockPanel();
        apply.Style = (Style)Application.Current.FindResource("Primary");
        apply.Content = windowsEqualizer ? "Apply Windows EQ" : "Apply to headphones";
        AutomationProperties.SetName(apply, windowsEqualizer ? "Apply Windows equaliser" : "Apply equaliser to headphones");
        DockPanel.SetDock(apply, Dock.Right); actions.Children.Add(apply);
        var revert = SmallButton("Revert edits");
        revert.Click += (_, _) => SetGains(original);
        revert.HorizontalAlignment = HorizontalAlignment.Left;
        actions.Children.Add(revert); Children.Add(actions);
        var saved = new Grid { Margin = new(0, 19, 0, 0) };
        saved.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        saved.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var picker = new ComboBox { DisplayMemberPath = "Name", Margin = new(0, 0, 8, 0), ToolTip = "Saved profiles for this headphone model" };
        AutomationProperties.SetName(picker, "Saved equaliser profile");
        saved.Children.Add(picker);
        var load = SmallButton("Load profile"); Grid.SetColumn(load, 1); saved.Children.Add(load);
        void Reload()
        {
            try { picker.ItemsSource = profiles.Read(model).Where(p => p.BandHz.SequenceEqual(Frequencies)).ToArray(); picker.SelectedIndex = picker.Items.Count > 0 ? 0 : -1; load.IsEnabled = picker.Items.Count > 0; }
            catch (Exception ex) { feedback.Text = "Could not read saved profiles: " + ex.Message; load.IsEnabled = false; }
        }
        load.Click += (_, _) => { if (picker.SelectedItem is EqualizerProfile p) SetGains(p.GainsDb); };
        Children.Add(saved);
        var saveRow = new Grid { Margin = new(0, 9, 0, 0) };
        saveRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var profileName = new TextBox { MaxLength = 60, Margin = new(0, 0, 8, 0), ToolTip = "Name your profile" };
        AutomationProperties.SetName(profileName, "Equaliser profile name");
        saveRow.Children.Add(profileName);
        var save = SmallButton("Save profile"); Grid.SetColumn(save, 1); saveRow.Children.Add(save);
        save.Click += (_, _) =>
        {
            try { profiles.Save(model, new(profileName.Text.Trim(), Frequencies, dials.Select(s => s.Value).ToArray())); Reload(); feedback.Text = windowsEqualizer ? "Profile saved on this PC. Apply saves it to the selected Windows output." : "Profile saved on this PC. Press Apply to use it on your headphones."; }
            catch (Exception ex) { feedback.Text = "Could not save profile: " + ex.Message; }
        };
        Children.Add(saveRow);
        updating = false;
        SetGains(draft?.Length == Frequencies.Length ? draft.Select(v => v / factor).ToArray() : original, false);
        Reload();
    }

    private void SetGains(double[] values, bool edited = true)
    {
        updating = true;
        for (var i = 0; i < dials.Length; i++) dials[i].Value = Math.Clamp(Math.Round(values[i] * factor) / factor, minimum, maximum);
        updating = false;
        Update(edited);
    }

    private void Update(bool edited = true)
    {
        if (updating) return;
        for (var i = 0; i < entries.Length; i++) entries[i].Text = FormatGain(dials[i].Value);
        graph.Gains = dials.Select(s => s.Value).ToArray();
        graph.InvalidateVisual();
        var changed = !graph.Gains.SequenceEqual(original);
        feedback.Text = changed ? "Unapplied changes · press Apply when you are ready." : windowsEqualizer ? "Windows EQ draft · Apply saves configuration for this output." : hasDeviceCurve ? "Saved headphone curve · edits take effect when you press Apply." : "New flat draft · your current preset stays active until you press Apply.";
        if (edited) Edited?.Invoke(Gains);
    }

    private static Button SmallButton(string text) => new() { Content = text, FontSize = 11, Padding = new(12, 7, 12, 7), MinHeight = 33, Margin = new(0, 0, 6, 0) };
    private static string FormatFrequency(int hz) => hz >= 1000 ? $"{hz / 1000d:0.#} kHz" : hz.ToString(CultureInfo.InvariantCulture) + " Hz";
    private static string FormatGain(double gain) => (gain == 0 ? 0 : gain).ToString("0.#", CultureInfo.CurrentCulture);

    private sealed class GainCurve(int[] frequencies, double minimum, double maximum) : FrameworkElement
    {
        internal double[] Gains { get; set; } = new double[frequencies.Length];
        protected override void OnRender(DrawingContext dc)
        {
            var width = Math.Max(1, ActualWidth - 54); var height = Math.Max(1, ActualHeight - 35);
            double X(int i) => 37 + Math.Log((double)frequencies[i] / frequencies[0]) / Math.Log((double)frequencies[^1] / frequencies[0]) * width;
            double Y(double gain) => 10 + (maximum - gain) / (maximum - minimum) * height;
            foreach (var gain in new[] { maximum, 0, minimum })
            {
                dc.DrawLine(new Pen(Brush("Stroke"), 1), new(37, Y(gain)), new(37 + width, Y(gain)));
                var text = new FormattedText(gain.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brush("Subtle"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new(4, Y(gain) - 7));
            }
            var line = new StreamGeometry();
            using (var c = line.Open()) { c.BeginFigure(new(X(0), Y(Gains[0])), false, false); for (var i = 1; i < Gains.Length; i++) c.LineTo(new(X(i), Y(Gains[i])), true, false); }
            dc.DrawGeometry(null, new Pen(Brush("Accent"), 2), line);
            for (var i = 0; i < Gains.Length; i++) dc.DrawEllipse(Brush("Accent"), null, new(X(i), Y(Gains[i])), 3, 3);
        }
    }
}
