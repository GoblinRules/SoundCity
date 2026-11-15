using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using SoundCity.App;
using SoundCity.Core;

// WPF input contracts only. No windows, endpoint writes or Bluetooth connections.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App(); app.InitializeComponent();
        var dial = new RotaryDial { Minimum = -12, Maximum = 12, SmallChange = 0.1, LargeChange = 1 };
        AutomationProperties.SetName(dial, "Test frequency gain");
        var peer = UIElementAutomationPeer.CreatePeerForElement(dial)!;
        var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue)!;
        range.SetValue(3.24);
        Check(dial.Value == 3.2 && peer.GetName() == "Test frequency gain" && range.SmallChange == 0.1, "Accessible range and tenth-decibel precision");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -13, 13 })
        {
            try { range.SetValue(invalid); throw new Exception("Dial accepted invalid input"); }
            catch (ArgumentOutOfRangeException) { Check(dial.Value == 3.2, "Invalid input leaves value unchanged"); }
        }
        Press(dial, Key.Up); Check(dial.Value == 3.3, "Arrow key advances one small step");
        Press(dial, Key.PageDown); Check(dial.Value == 2.3, "Page key reduces gain by one large step");
        Press(dial, Key.Home); Press(dial, Key.Down); Check(dial.Value == -12, "Keyboard input respects minimum");
        Press(dial, Key.End); Press(dial, Key.Up); Check(dial.Value == 12, "Keyboard input respects maximum");
        dial.IsEnabled = false;
        try { range.SetValue(0); throw new Exception("Disabled dial accepted input"); }
        catch (ElementNotEnabledException) { Check(range.IsReadOnly && dial.Value == 12, "Disabled dial rejects accessible changes"); }
        Press(dial, Key.Down); Check(dial.Value == 12, "Disabled dial ignores keyboard changes");
        var boost = new RotaryDial { Minimum = 0, Maximum = 12, SmallChange = 0.5 };
        var boostRange = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(boost)!.GetPattern(PatternInterface.RangeValue)!;
        boostRange.SetValue(1.3);
        Check(boost.Value == 1.5, "Boost snaps to half-decibel increments");
        var parent = new System.Windows.Controls.StackPanel();
        parent.Children.Add(boost);
        var pageScrolls = 0;
        parent.MouseWheel += (_, _) => pageScrolls++;
        Check(!boost.IsKeyboardFocused && Wheel(boost, 120).Handled && boost.Value == 2, "Wheel adjusts an unfocused dial without a click");
        Wheel(boost, -240); Check(boost.Value == 1, "Wheel down honours multiple detents");
        for (var i = 0; i < 3; i++) Wheel(boost, 30);
        Check(boost.Value == 1, "Smooth-wheel partial deltas do not over-adjust gain");
        Wheel(boost, 30); Check(boost.Value == 1.5, "Smooth-wheel deltas accumulate into one step");
        Wheel(boost, 12000); Check(boost.Value == 12 && pageScrolls == 0, "Wheel clamps at the maximum without scrolling the page");
        Wheel(boost, -12000); Check(boost.Value == 0 && pageScrolls == 0, "Wheel clamps at the minimum without scrolling the page");
        boost.IsEnabled = false;
        Check(!Wheel(boost, 120).Handled && boost.Value == 0 && pageScrolls == 1, "Disabled dial ignores wheel adjustment and allows page scrolling");
        dial.IsEnabled = true; range.SetValue(0);
        Wheel(dial, 120); Check(dial.Value == 0.1, "Windows EQ wheel retains tenth-decibel precision");
        CheckViews();
    }

    private static void CheckViews()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SoundCity-ui-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "views.json");
        Directory.CreateDirectory(folder);
        try
        {
            var preferences = new ControlViewPreferences(path);
            var draft = new[] { 37, -24, 0, 0, 0, 0, 0, 0, 0, 0 };
            var setting = new DeviceSetting("windowsEqualizer", "windowsEqualizer", "equalizer", JsonSerializer.SerializeToElement(new { bandHz = EqualizerApo.EqBands, min = -120, max = 120, fractionDigits = 1 }), JsonSerializer.SerializeToElement(new int[10]));
            var apply = new Button();
            var applies = 0; apply.Click += (_, _) => applies++;
            var editor = new EqualizerEditor(setting, "test", new EqualizerProfiles(folder), draft, "Test curve", apply, true, preferences);
            var edits = 0; editor.Edited += _ => edits++;
            var host = new Border { Child = editor, Width = 590 };
            void Layout() { host.Measure(new Size(590, double.PositiveInfinity)); host.Arrange(new Rect(0, 0, 590, host.DesiredSize.Height)); host.UpdateLayout(); }
            Layout();
            var dials = Descendants(editor).OfType<RotaryDial>().ToArray();
            var sliders = Descendants(editor).OfType<GainSlider>().ToArray();
            var options = Descendants(editor).OfType<RadioButton>().ToArray();
            options.Single(b => (string)b.Content == "Sliders").IsChecked = true;
            Layout();
            Check(sliders.Length == 10 && sliders.All(s => s.Visibility == Visibility.Visible) && dials.All(d => d.Visibility == Visibility.Collapsed), "EQ switches to ten sliders without rebuilding the editor");
            Check(editor.Gains.SequenceEqual(draft) && edits == 0 && applies == 0, "Switching views preserves a fractional unapplied curve and does not apply it");
            Check(new ControlViewPreferences(path).UsesSliders(false), "EQ view choice survives reloading preferences");
            sliders[0].Value = 4.2;
            Check(editor.Gains[0] == 42 && dials[0].Value == 4.2 && edits == 1, "Slider editing updates the shared EQ draft exactly once");
            Wheel(sliders[0], -120);
            Check(editor.Gains[0] == 41, "Sliders support mouse-wheel gain steps too");
            options.Single(b => (string)b.Content == "Dials").IsChecked = true;
            Layout();
            Check(dials[0].Value == 4.1 && dials.All(d => d.Visibility == Visibility.Visible) && sliders.All(s => s.Visibility == Visibility.Collapsed) && applies == 0, "Returning to dials preserves slider edits without applying audio");
            dials[1].Value = -1.3;
            Check(sliders[1].Value == -1.3 && editor.Gains[1] == -13, "Dial edits remain synchronized with the hidden sliders");
            var boostMode = new GainViewSwitch(true, preferences);
            ((StackPanel)boostMode.Child).Children.OfType<RadioButton>().Single(b => (string)b.Content == "Sliders").IsChecked = true;
            var saved = new ControlViewPreferences(path);
            Check(saved.UsesSliders(true) && !saved.UsesSliders(false), "Boost and EQ remember independent view choices");
            Check(new GainViewSwitch(true, saved).UsesSliders, "A newly opened boost view restores its slider choice");
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(folder); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Press(RotaryDial dial, Key key) => typeof(RotaryDial).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dial, new object[] { new KeyEventArgs(Keyboard.PrimaryDevice, new TestSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent } });
    private static MouseWheelEventArgs Wheel(UIElement dial, int delta)
    {
        var scroll = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.MouseWheelEvent };
        dial.RaiseEvent(scroll);
        return scroll;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
    private sealed class TestSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
