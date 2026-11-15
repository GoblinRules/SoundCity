using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoundCity.App;

/// <summary>Visual gain rail; the Slider's normal Track handles all input.</summary>
public sealed class GainTrack : FrameworkElement
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(GainTrack), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(GainTrack), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(GainTrack), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(GainTrack), new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsDirectionReversedProperty = DependencyProperty.Register(nameof(IsDirectionReversed), typeof(bool), typeof(GainTrack), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThumbSizeProperty = DependencyProperty.Register(nameof(ThumbSize), typeof(double), typeof(GainTrack), new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public bool IsDirectionReversed { get => (bool)GetValue(IsDirectionReversedProperty); set => SetValue(IsDirectionReversedProperty, value); }
    public double ThumbSize { get => (double)GetValue(ThumbSizeProperty); set => SetValue(ThumbSizeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Maximum <= Minimum || ActualWidth <= 0 || ActualHeight <= 0) return;
        var vertical = Orientation == Orientation.Vertical;
        var axis = vertical ? ActualHeight : ActualWidth;
        var inset = Math.Min(ThumbSize / 2, axis / 2);
        Point Position(double gain)
        {
            var fraction = Math.Clamp((gain - Minimum) / (Maximum - Minimum), 0, 1);
            if (IsDirectionReversed) fraction = 1 - fraction;
            var offset = inset + fraction * (axis - 2 * inset);
            return vertical ? new(ActualWidth / 2, ActualHeight - offset) : new(offset, ActualHeight / 2);
        }
        var start = Position(Minimum);
        var end = Position(Maximum);
        var zero = Position(Math.Clamp(0, Minimum, Maximum));
        var current = Position(Value);
        var gradient = GainPalette.AlongTrack(Minimum, Maximum, start, end);
        var pen = new Pen(gradient, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(new Pen((Brush)FindResource("Stroke"), 4), start, end);
        dc.PushOpacity(0.25); dc.DrawLine(pen, start, end); dc.Pop();
        if (Minimum < 0 && Maximum > 0)
            dc.DrawLine(new Pen((Brush)FindResource("Muted"), 1), vertical ? new(zero.X - 5, zero.Y) : new(zero.X, zero.Y - 5), vertical ? new(zero.X + 5, zero.Y) : new(zero.X, zero.Y + 5));
        if (zero != current) dc.DrawLine(pen, zero, current);
    }
}
