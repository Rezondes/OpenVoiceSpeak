using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using OVS.Client.Converters;
using OVS.Client.Localization;

namespace OVS.Client.Controls;

/// <summary>
/// Package 164: a small icon button beside a full fingerprint that puts exactly its <see cref="Text"/> on the clipboard;
/// its tooltip says "Kopiert" for 2 s afterwards. Hidden without a value and in streamer mode (Package 160), so it never
/// copies what the screen hides.
/// </summary>
internal sealed class CopyButton : Button
{
    /// <summary>The value the button copies.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<CopyButton, string?>(nameof(Text));

    /// <summary>How long the tooltip says "Kopiert".</summary>
    public static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2);

    int copies; // only the last copy's 2 s put the tooltip back
    string label = Strings.Ui_CopyFingerprint;

    /// <summary>An icon button with the copy icon.</summary>
    public CopyButton()
    {
        Classes.Add("icon");
        var icon = new PathIcon();
        icon.Classes.Add("sm");
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable("Icon.Copy", value => value as Geometry));
        Content = icon;
        Label = label;
        IsVisible = false;
        Click += (_, _) => _ = CopyAsync();
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The value the button copies.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Package 170: what the button copies, as its tooltip and name; the fingerprint by default.</summary>
    public string Label
    {
        get => label;
        set
        {
            label = value;
            ToolTip.SetTip(this, value);
            AutomationProperties.SetName(this, value);
        }
    }

    /// <summary>The clock of the 2 s; the tests set a manual one.</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System; // clock: real time, a control the views make; the tests set a manual one

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == PrivacyConverters.HideProperty)
            IsVisible = !string.IsNullOrEmpty(Text) && !PrivacyConverters.GetHide(this);
    }

    async Task CopyAsync()
    {
        if (string.IsNullOrEmpty(Text) || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(Text);
        int copy = ++copies;
        ToolTip.SetTip(this, Strings.Ui_Copied);
        await Task.Delay(CopiedFor, Time);
        if (copy == copies) ToolTip.SetTip(this, label);
    }
}
