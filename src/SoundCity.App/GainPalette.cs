using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SoundCity.App;

internal static class GainPalette
{
    private static readonly SolidColorBrush[] Colors = CreateColors();
    internal static Brush At(double gain, double minimum, double maximum)
    {
        var amount = gain < 0 ? (minimum < 0 ? gain / minimum : 0) : (maximum > 0 ? gain / maximum : 0);
        return Colors[(int)Math.Round(Math.Clamp(amount, 0, 1) * (Colors.Length - 1))];
    }

    internal static Brush AlongTrack(double minimum, double maximum, Point start, Point end)
    {
        // Absolute coordinates keep colours anchored to the scale, even when
        // only the section between zero and the current value is drawn.
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = start, EndPoint = end };
        foreach (var gain in new[] { minimum, minimum / 2, 0, maximum / 2, maximum }.Where(g => g >= minimum && g <= maximum).Distinct().OrderBy(g => g))
            brush.GradientStops.Add(new(((SolidColorBrush)At(gain, minimum, maximum)).Color, (gain - minimum) / (maximum - minimum)));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush[] CreateColors()
    {
        var green = Color.FromRgb(117, 219, 139);
        var amber = Color.FromRgb(255, 193, 87);
        var red = Color.FromRgb(255, 101, 101);
        var brushes = new SolidColorBrush[257];
        for (var i = 0; i < brushes.Length; i++)
        {
            var t = i / 256d;
            var from = t <= 0.5 ? green : amber;
            var to = t <= 0.5 ? amber : red;
            t = t <= 0.5 ? t * 2 : (t - 0.5) * 2;
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
            brushes[i] = new(Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B)));
            brushes[i].Freeze();
        }
        return brushes;
    }
}
