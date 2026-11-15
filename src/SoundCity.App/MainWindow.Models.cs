using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SoundCity.Core;

namespace SoundCity.App;

public partial class MainWindow
{
    private async void IdentifyClick(object sender, RoutedEventArgs e)
    {
        if (busy || demo) return;
        await RunAsync("Finding paired devices…", async token =>
        {
            var paired = await discovery.FindPairedAsync(token);
            var saved = modelMappings.Read();
            var window = new Window { Owner = this, Title = "Identify headphones", Width = 580, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, WindowStartupLocation = WindowStartupLocation.CenterOwner, Style = (Style)FindResource("DarkWindow") };
            var content = new StackPanel { Margin = new(26) };
            window.Content = new Border { BorderBrush = Theme("Stroke"), BorderThickness = new(1), Background = Theme("Canvas"), Child = content };
            var title = new TextBlock { Text = "Give your headphones the right controls", FontSize = 21, FontWeight = FontWeights.SemiBold };
            title.MouseLeftButtonDown += (_, _) => window.DragMove(); content.Children.Add(title);
            content.Children.Add(new TextBlock { Text = "Choose a paired device and its exact model. This is useful for names such as ‘My AirPods Pro’ or ‘Parrot Zik’. Check the model in your iPhone settings, phone app or on the headphones.", Foreground = Theme("Muted"), Margin = new(0, 12, 0, 18), LineHeight = 22 });
            var devices = new ComboBox { ItemsSource = paired, DisplayMemberPath = "Name", Margin = new(0, 0, 0, 12) };
            AutomationProperties.SetName(devices, "Paired headphones to identify"); content.Children.Add(devices);
            var models = new ComboBox { ItemsSource = DeviceCatalog.All.Select(p => new OptionChoice(p.Id, $"{p.Brand} {p.Name} · {p.ModelCode}")).ToArray(), Margin = new(0, 0, 0, 12) };
            AutomationProperties.SetName(models, "Exact headphone model"); content.Children.Add(models);
            var confirm = new CheckBox { Content = "I checked that this is the exact model", Margin = new(0, 0, 0, 16) }; content.Children.Add(confirm);
            var feedback = new TextBlock { Foreground = Theme("Muted"), Margin = new(0, 0, 0, 16) }; content.Children.Add(feedback);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Close", IsCancel = true, Style = (Style)FindResource("Ghost"), Margin = new(0, 0, 10, 0) };
            cancel.Click += (_, _) => window.Close(); actions.Children.Add(cancel);
            var remove = new Button { Content = "Forget selection", Margin = new(0, 0, 10, 0), IsEnabled = false };
            var save = new Button { Content = "Save model", Style = (Style)FindResource("Primary"), IsEnabled = false };
            actions.Children.Add(remove); actions.Children.Add(save); content.Children.Add(actions);
            void Update()
            {
                save.IsEnabled = devices.SelectedItem is PairedDevice && models.SelectedItem is OptionChoice && confirm.IsChecked == true;
                remove.IsEnabled = devices.SelectedItem is PairedDevice p && saved.ContainsKey(DeviceCatalog.FormatAddress(p.BluetoothAddress));
            }
            devices.SelectionChanged += (_, _) =>
            {
                confirm.IsChecked = false;
                if (devices.SelectedItem is PairedDevice p)
                {
                    var model = DeviceProfileMappings.Resolve(p.Name, p.BluetoothAddress, saved);
                    models.SelectedItem = models.Items.Cast<OptionChoice>().FirstOrDefault(m => m.Id == model?.Id);
                    models.IsEnabled = DeviceCatalog.Match(p.Name) is null;
                    feedback.Text = models.IsEnabled ? "Saved on this PC. This does not rename or change the headphones." : "This exact Bluetooth name already identifies its model.";
                }
                Update();
            };
            models.SelectionChanged += (_, _) => { confirm.IsChecked = false; Update(); };
            confirm.Checked += (_, _) => Update(); confirm.Unchecked += (_, _) => Update();
            var changed = false;
            save.Click += (_, _) =>
            {
                if (devices.SelectedItem is not PairedDevice p || models.SelectedItem is not OptionChoice m || confirm.IsChecked != true) return;
                try { modelMappings.Save(p.BluetoothAddress, DeviceCatalog.All.Single(v => v.Id == m.Id)); changed = true; window.Close(); }
                catch (Exception ex) { feedback.Text = ex.Message; }
            };
            remove.Click += (_, _) =>
            {
                if (devices.SelectedItem is not PairedDevice p) return;
                try { modelMappings.Save(p.BluetoothAddress, null); changed = true; window.Close(); }
                catch (Exception ex) { feedback.Text = ex.Message; }
            };
            devices.SelectedItem = paired.FirstOrDefault(p => p.BluetoothAddress == Selected?.BluetoothAddress) ?? paired.FirstOrDefault();
            operation?.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
            window.ShowDialog();
            operation?.CancelAfter(TimeSpan.FromSeconds(90));
            if (changed) await DiscoverAsync(token);
        });
    }
}
