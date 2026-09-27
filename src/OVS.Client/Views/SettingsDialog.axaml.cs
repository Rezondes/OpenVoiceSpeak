using Avalonia.Controls;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm) vm.CloseRequested += ok => Close(ok);
        };
    }
}
