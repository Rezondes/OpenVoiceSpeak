using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 72: lists what goes, so nobody deletes a user by accident.</summary>
internal static class DeleteUserDialog
{
    internal static async Task<bool> ShowAsync(OverlayHost overlay, string nickname)
    {
        var items = new[] { Strings.Dlg_DeleteUserRecord, Strings.Dlg_DeleteUserGroups, Strings.Dlg_DeleteUserStats, Strings.Dlg_DeleteUserBans };
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
        list.Children.AddRange(items.Select(item => CreateText("\u2022 " + item)));
        var body = CreateStack(CreateText(Strings.Dlg_DeleteUserIntro), list, CreateText(Strings.Dlg_DeleteUserHint, "muted"));
        return await DialogFrame.ShowAsync(overlay, TextFormat.Of(Strings.Dlg_DeleteUserTitle, nickname), "PersonDelete", body, () => "ok",
            new(Strings.Dlg_DeleteForever, OkIsDefault: false, Kind: DialogKind.Danger)) is not null;
    }
}
