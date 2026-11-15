using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoundCity.App;

public sealed class GainViewSwitch : Border
{
    private readonly RadioButton dials, sliders;
    public bool UsesSliders => sliders.IsChecked == true;
    public event Action<bool>? Changed;

    public GainViewSwitch(bool boost, ControlViewPreferences? preferences = null)
    {
        preferences ??= ControlViewPreferences.Current;
        Background = (Brush)Application.Current.FindResource("Input");
        BorderBrush = (Brush)Application.Current.FindResource("Stroke");
        BorderThickness = new(1); CornerRadius = new(8); Padding = new(3);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Child = row;
        var group = "GainView-" + Guid.NewGuid().ToString("N");
        var style = (Style)Application.Current.FindResource("GainViewOption");
        dials = new() { Content = "Dials", GroupName = group, Style = style };
        sliders = new() { Content = "Sliders", GroupName = group, Style = style };
        AutomationProperties.SetName(dials, (boost ? "Volume boost" : "Equaliser") + " dial view");
        AutomationProperties.SetName(sliders, (boost ? "Volume boost" : "Equaliser") + " slider view");
        row.Children.Add(dials); row.Children.Add(sliders);
        if (preferences.UsesSliders(boost)) sliders.IsChecked = true; else dials.IsChecked = true;
        void Select(bool useSliders)
        {
            try { preferences.Save(boost, useSliders); ToolTip = null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { ToolTip = "View changed for this session. The preference could not be saved."; }
            Changed?.Invoke(useSliders);
        }
        dials.Checked += (_, _) => Select(false);
        sliders.Checked += (_, _) => Select(true);
    }
}
