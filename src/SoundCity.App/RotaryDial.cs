using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundCity.App;

/// <summary>A relative-drag dial: editing a value never applies an audio setting.</summary>
public sealed class RotaryDial : RangeBase
{
    public static readonly DependencyProperty ShowValueProperty = DependencyProperty.Register(nameof(ShowValue), typeof(bool), typeof(RotaryDial), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowValue { get => (bool)GetValue(ShowValueProperty); set => SetValue(ShowValueProperty, value); }
    private Point lastPointer;
    private double dragValue;
    private bool dragging;
    private int wheelRemainder;

    static RotaryDial()
    {
        FocusableProperty.OverrideMetadata(typeof(RotaryDial), new FrameworkPropertyMetadata(true));
        IsEnabledProperty.OverrideMetadata(typeof(RotaryDial), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public RotaryDial()
    {
        Width = Height = 96;
        Cursor = Cursors.SizeNS;
        FocusVisualStyle = null;
        ToolTip = "Mouse wheel or drag to adjust · Shift-drag for fine control · Double-click to reset";
        AutomationProperties.SetHelpText(this, "Hover and use the mouse wheel, or drag vertically to adjust. No click is needed for the wheel. Shift-drag adjusts finely. Arrow keys step, Page Up and Page Down make larger changes, Home selects minimum, End selects maximum. Double-click resets to zero. Changes take effect after Apply.");
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => { wheelRemainder = 0; InvalidateVisual(); };
        IsEnabledChanged += (_, _) => wheelRemainder = 0;
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => InvalidateVisual();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
        UIElementAutomationPeer.FromElement(this)?.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum) { base.OnMinimumChanged(oldMinimum, newMinimum); InvalidateVisual(); }
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum) { base.OnMaximumChanged(oldMaximum, newMaximum); InvalidateVisual(); }

    internal void SetDialValue(double requested)
    {
        if (!double.IsFinite(requested)) throw new ArgumentOutOfRangeException(nameof(requested));
        var step = SmallChange > 0 ? SmallChange : 1;
        var snapped = Minimum + Math.Round((requested - Minimum) / step, MidpointRounding.AwayFromZero) * step;
        SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(snapped, 8), Minimum, Maximum));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        Focus();
        if (e.ClickCount == 2) { SetDialValue(0); e.Handled = true; return; }
        lastPointer = e.GetPosition(this);
        dragValue = Value;
        dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || !IsEnabled) return;
        var point = e.GetPosition(this);
        var fine = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.1 : 1;
        dragValue = Math.Clamp(dragValue + (lastPointer.Y - point.Y) * (Maximum - Minimum) / 160 * fine, Minimum, Maximum);
        lastPointer = point;
        SetDialValue(dragValue);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = false; base.OnLostMouseCapture(e); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsEnabled) { base.OnMouseWheel(e); return; }
        // Handle the wheel at the dial, before the surrounding page can scroll.
        // Keep partial deltas for smooth wheels and honour multiple detents.
        var delta = (long)wheelRemainder + e.Delta;
        var steps = delta / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder = (int)(delta % Mouse.MouseWheelDeltaForOneLine);
        if (steps != 0) SetDialValue(Value + steps * SmallChange);
        if (dragging) dragValue = Value;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsEnabled && e.Key is Key.Up or Key.Right or Key.Down or Key.Left or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            SetDialValue(e.Key switch
            {
                Key.Up or Key.Right => Value + SmallChange,
                Key.Down or Key.Left => Value - SmallChange,
                Key.PageUp => Value + LargeChange,
                Key.PageDown => Value - LargeChange,
                Key.Home => Minimum,
                _ => Maximum
            });
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DialAutomationPeer(this);

    private sealed class DialAutomationPeer(RotaryDial owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(RotaryDial);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => owner.LargeChange;
        public double SmallChange => owner.SmallChange;
        public double Maximum => owner.Maximum;
        public double Minimum => owner.Minimum;
        public double Value => owner.Value;
        public void SetValue(double value)
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            owner.SetDialValue(value);
        }
    }

    private Brush Theme(string name) => (Brush)FindResource(name);
    private static SolidColorBrush ColorBrush(string value) => new((Color)ColorConverter.ConvertFromString(value));
    private static Point Polar(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180;
        return new(50 + Math.Cos(radians) * radius, 50 + Math.Sin(radians) * radius);
    }

    private static void Arc(DrawingContext dc, Brush brush, double width, double from, double to, double radius)
    {
        if (Math.Abs(to - from) < 0.001) return;
        var path = new StreamGeometry();
        using (var context = path.Open())
        {
            context.BeginFigure(Polar(from, radius), false, false);
            context.ArcTo(Polar(to, radius), new(radius, radius), 0, Math.Abs(to - from) > 180, to > from ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
        }
        path.Freeze();
        dc.DrawGeometry(null, new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 100, size / 100));
        dc.PushOpacity(IsEnabled ? 1 : 0.35);
        var span = Maximum - Minimum;
        double Angle(double value) => 135 + (span > 0 ? (value - Minimum) / span : 0) * 270;
        var angle = Angle(Value);
        var zero = Angle(Math.Clamp(0, Minimum, Maximum));
        for (var i = 0; i <= 12; i++)
        {
            var degrees = 135 + i * 22.5;
            dc.DrawLine(new Pen(i == 6 && Minimum < 0 ? Theme("Muted") : Theme("Stroke"), i % 3 == 0 ? 1.4 : 1), Polar(degrees, 45), Polar(degrees, i % 3 == 0 ? 49 : 47));
        }
        Arc(dc, Theme("Stroke"), 3, 135, 405, 40.5);
        var segmentCount = (int)Math.Ceiling(Math.Abs(angle - zero) / 2);
        for (var i = 0; i < segmentCount; i++)
        {
            var from = zero + (angle - zero) * i / segmentCount;
            var to = zero + (angle - zero) * (i + 1) / segmentCount;
            var gain = Minimum + (((from + to) / 2 - 135) / 270) * span;
            var color = GainPalette.At(gain, Minimum, Maximum);
            dc.PushOpacity(0.14);
            Arc(dc, color, 7, from, to, 40.5);
            dc.Pop();
            Arc(dc, color, 3, from, to, 40.5);
        }
        dc.DrawEllipse(ColorBrush("#080B10"), null, new(50, 53), 35, 35);
        var rim = new LinearGradientBrush(Color.FromRgb(66, 76, 91), Color.FromRgb(15, 19, 27), 90);
        dc.DrawEllipse(rim, new Pen(ColorBrush("#080B10"), 1), new(50, 50), 34, 34);
        var face = new LinearGradientBrush(Color.FromRgb(38, 46, 60), Color.FromRgb(17, 22, 31), 90);
        dc.DrawEllipse(face, new Pen(ColorBrush(IsMouseOver ? "#656A70" : "#394252"), 0.7), new(50, 50), 31.5, 31.5);
        dc.DrawEllipse(null, new Pen(ColorBrush("#212938"), 0.7), new(50, 50), 27, 27);
        dc.DrawLine(new Pen(GainPalette.At(Value, Minimum, Maximum), 2.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Polar(angle, 23), Polar(angle, 28));
        void Label(string label, double fontSize, Brush brush, double y, FontWeight weight)
        {
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new(50 - text.Width / 2, y));
        }
        if (ShowValue)
        {
            Label(Value.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture), 16, Theme("Ink"), 38, FontWeights.SemiBold);
            Label("dB", 7, Theme("Muted"), 57, FontWeights.Normal);
        }
        else Label("dB", 11, Theme("Muted"), 43, FontWeights.Normal);
        if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(Theme("Accent"), 1) { DashStyle = DashStyles.Dot }, new(50, 50), 36.5, 36.5);
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
