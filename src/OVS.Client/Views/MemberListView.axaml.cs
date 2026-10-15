using Avalonia.Controls;
using Avalonia.VisualTree;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Package 167: the member list beside the chat.</summary>
internal sealed partial class MemberListView : UserControl
{
    /// <summary>The list; its view model comes from the main window.</summary>
    public MemberListView() => InitializeComponent();

    /// <summary>A right click on a member opens the same menu as that user's row in the channel tree.</summary>
    void OnMemberContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (sender is not Control { DataContext: UserViewModel user } row || this.FindAncestorOfType<MainWindow>() is not { } main) return;
        args.Handled = main.OpenUserMenu(user, row);
    }
}
