using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoundCity.App;
using SoundCity.Core;

// Render the application's actual WPF controls with labelled demo data.
// No window is shown, no discovery runs, no Apply button is invoked, and profiles
// and display preferences use an isolated directory. Nothing touches live audio.
internal static class Program
{
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var root = new DirectoryInfo(Environment.CurrentDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Run from the SoundCity checkout.");
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root.FullName, "docs", "images"));
        Directory.CreateDirectory(output);
        var scratch = Path.Combine(Path.GetTempPath(), "SoundCity-readme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var runner = new OpenScq30Runner(Path.Combine(root.FullName, "tools", "openscq30", "openscq30.exe"), Path.Combine(scratch, "demo.sqlite"));
        runner.RunAsync(new[] { "paired-devices", "add", "--mac-address", "00:00:00:00:00:01", "--model", "SoundcoreA3954", "--demo" }).GetAwaiter().GetResult();
        var backend = new SoundcoreBackend(runner);
        var samples = backend.ReadAsync("00:00:00:00:00:01").GetAwaiter().GetResult();
        var app = new App(); app.InitializeComponent();
        var views = new ControlViewPreferences(Path.Combine(scratch, "views.json"));
        var window = new MainWindow(true, controlViews: views);
        try
        {
            typeof(MainWindow).GetField("eqProfiles", Instance)!.SetValue(window, new EqualizerProfiles(Path.Combine(scratch, "profiles")));
            typeof(MainWindow).GetField("runner", Instance)!.SetValue(window, runner);
            typeof(MainWindow).GetField("backend", Instance)!.SetValue(window, backend);
            // Selection normally starts a device read. Suppress it while seeding the demo.
            typeof(MainWindow).GetField("busy", Instance)!.SetValue(window, true);
            var devices = (ListBox)window.FindName("DevicesList");
            devices.ItemsSource = new[] { new Headphone("Demo Liberty 4 Pro", 1, "demo", false, DeviceCatalog.Liberty4Pro) };
            devices.SelectedIndex = 0;
            typeof(MainWindow).GetMethod("SetBusy", Instance)!.Invoke(window, new object[] { false });
            var category = typeof(MainWindow).GetNestedType("CategoryChoice", BindingFlags.NonPublic)!;
            var picker = (ComboBox)window.FindName("CategoryPicker");
            var modeCategory = samples.Single(s => s.Id == "ambientSoundMode").Category;
            var modeChoice = Activator.CreateInstance(category, new object[] { modeCategory, Labels.Humanize(modeCategory) });
            var eqChoice = Activator.CreateInstance(category, new object[] { "windowsEqualizer", "Windows equaliser" });
            typeof(MainWindow).GetField("settings", Instance)!.SetValue(window, samples);
            picker.ItemsSource = new[] { modeChoice, eqChoice }; picker.SelectedIndex = 0;
            ((FrameworkElement)window.FindName("FilterBar")).Visibility = Visibility.Visible;
            ((TextBlock)window.FindName("Status")).Text = "Demo preview · example values · no audio settings are changed.";
            var content = (FrameworkElement)window.Content;
            Layout(content, 1360, 1000);
            Save(content, Path.Combine(output, "soundcity-desktop.png"));
            picker.SelectedIndex = 1;
            Layout(content, 1360, 1000);
            var editor = Descendants(content).OfType<FrameworkElement>().Single(e => e.GetType().Name == "EqualizerEditor");
            var values = new[] { 4d, 3, 2, 0, -2, -3, -1, 1, 3, 4 };
            var dials = Descendants(editor).OfType<RotaryDial>().ToArray();
            if (dials.Length != values.Length) throw new InvalidOperationException("Unexpected EQ band count.");
            for (var i = 0; i < dials.Length; i++) dials[i].Value = values[i];
            CaptureEditor(editor, Path.Combine(output, "equaliser-dials.png"));
            Descendants(editor).OfType<RadioButton>().Single(r => r.Content as string == "Sliders").IsChecked = true;
            CaptureEditor(editor, Path.Combine(output, "equaliser-sliders.png"));
            if (window.IsLoaded) throw new InvalidOperationException("The documentation renderer must not open a window.");
            Console.WriteLine("Rendered three application images using isolated demo data; no hardware access or audio writes.");
        }
        finally
        {
            window.Close();
            // Only the unique temporary directory created above is removed.
            Directory.Delete(scratch, true);
        }
    }

    private static void CaptureEditor(FrameworkElement editor, string path)
    {
        var parent = (Panel)editor.Parent;
        var index = parent.Children.IndexOf(editor);
        parent.Children.RemoveAt(index);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "WINDOWS EQUALISER · DEMO CURVE", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("Accent"), Margin = new(0, 0, 0, 18) });
        content.Children.Add(editor);
        var host = new Border { Background = Brush("Canvas"), Padding = new(30), Child = content };
        host.SetValue(Control.ForegroundProperty, Brush("Ink"));
        host.SetValue(Control.FontFamilyProperty, new FontFamily("Segoe UI"));
        host.SetValue(Control.FontSizeProperty, 13d);
        Layout(host, 1000, double.PositiveInfinity);
        Save(host, path);
        content.Children.Remove(editor);
        parent.Children.Insert(index, editor);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, double.IsPositiveInfinity(height) ? element.DesiredSize.Height : height));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    }
    private static void Save(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
        Console.WriteLine($"{Path.GetFileName(path)}: {bitmap.PixelWidth} × {bitmap.PixelHeight}");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
