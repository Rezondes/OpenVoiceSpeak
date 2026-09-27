using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OVS.Client.Views;

public partial class AdminDialog : Window
{
    public AdminDialog()
    {
        InitializeComponent();
        // Hidden tabs stay selectable; start on the first one the user may see.
        Opened += (_, _) => Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.IsVisible);
    }

    void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
