using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundCity.App;

public sealed class GainSlider : Slider
{
    private static readonly DependencyPropertyKey GainBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(GainBrush), typeof(Brush), typeof(GainSlider), new PropertyMetadata(GainPalette.At(0, 0, 1)));
    public static readonly DependencyProperty GainBrushProperty = GainBrushPropertyKey.DependencyProperty;
    public Brush GainBrush => (Brush)GetValue(GainBrushProperty);
    private int wheelRemainder;
    public GainSlider()
    {
        MouseLeave += (_, _) => wheelRemainder = 0;
        IsEnabledChanged += (_, _) => wheelRemainder = 0;
        MouseDoubleClick += (_, e) => { SetCurrentValue(ValueProperty, Math.Clamp(0, Minimum, Maximum)); e.Handled = true; };
    }
    private void UpdateGainBrush() => SetValue(GainBrushPropertyKey, GainPalette.At(Value, Minimum, Maximum));
    protected override void OnValueChanged(double oldValue, double newValue) { base.OnValueChanged(oldValue, newValue); UpdateGainBrush(); }
    protected override void OnMinimumChanged(double oldMinimum, double newMinimum) { base.OnMinimumChanged(oldMinimum, newMinimum); UpdateGainBrush(); }
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum) { base.OnMaximumChanged(oldMaximum, newMaximum); UpdateGainBrush(); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsEnabled) { base.OnMouseWheel(e); return; }
        var delta = (long)wheelRemainder + e.Delta;
        var steps = delta / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder = (int)(delta % Mouse.MouseWheelDeltaForOneLine);
        if (steps != 0) SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(Value + steps * SmallChange, 8), Minimum, Maximum));
        e.Handled = true;
    }
}
