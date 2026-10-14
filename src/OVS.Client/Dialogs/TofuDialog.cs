using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OVS.Client.Converters;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Net;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Trust a server's certificate on first use, or after it changed (then red, with cancel as the default).</summary>
internal static class TofuDialog
{
    internal static async Task<bool> ShowAsync(OverlayHost overlay, TofuPrompt prompt)
    {
        var fingerprint = string.Join(" ", Enumerable.Range(0, prompt.Fingerprint.Length / 8).Select(index => prompt.Fingerprint.Substring(index * 8, 8)));
        bool mismatch = prompt.Result == TofuResult.Mismatch;
        // Package 160: streamer mode hides the address; the server's fingerprint stays, the user checks it here
        var host = PrivacyConverters.Address(prompt.Host, PrivacyConverters.GetHide(overlay));
        var text = mismatch
            ? TextFormat.Of(Strings.Tofu_Changed, host, prompt.Port)
            : TextFormat.Of(Strings.Tofu_First, host, prompt.Port);
        var code = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(8),
            Child = new DockPanel
            {
                Children =
                {
                    // Package 164: the 64 characters without the spaces, to compare elsewhere
                    DockAt(new CopyButton { Text = prompt.Fingerprint, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 0, 0) }, Dock.Right),
                    new SelectableTextBlock { Text = fingerprint, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
                },
            },
        };
        code.Bind(Border.BackgroundProperty, code.GetResourceObservable("Ovs.Surface"));
        Control message = CreateText(text);
        if (mismatch)
        {
            var icon = CreateIcon("Warning", "warning");
            icon.Margin = new Thickness(0, 1, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            var banner = new Border { Child = new DockPanel { Children = { DockAt(icon, Dock.Left), CreateText(text, "warning") } } };
            banner.Classes.Add("banner");
            banner.Classes.Add("warning");
            message = banner;
        }
        return await DialogFrame.ShowAsync(overlay, Strings.Tofu_Title, mismatch ? "Warning" : "LockClosed", CreateStack(message, CreateField("Fingerprint", code)), () => "ok",
            new(mismatch ? Strings.Tofu_TrustAnyway : Strings.Tofu_Trust, OkIsDefault: prompt.AcceptIsDefault,
                Kind: mismatch ? DialogKind.Danger : DialogKind.Normal)) is not null;
    }
}
