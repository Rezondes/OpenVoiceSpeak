using Avalonia;
using Avalonia.Controls;
using OVS.Client.Localization;

namespace OVS.Client.Controls;

/// <summary>
/// Package 143 (A153): "Fehler-ID kopieren" beside an error text; visible only when the text carries an unexpected server
/// error's ID (<see cref="ErrorTexts.IdIn"/>), and a click puts exactly that ID on the clipboard.
/// </summary>
internal sealed class ErrorIdButton : Button
{
    /// <summary>The error text shown beside it, as <see cref="ErrorTexts.For"/> made it.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ErrorIdButton, string?>(nameof(Text));

    /// <summary>A plain small button, styled like every other.</summary>
    public ErrorIdButton()
    {
        Classes.Add("subtle");
        Content = Strings.ErrorId_Copy;
        IsVisible = false;
        Click += (_, _) => _ = CopyAsync();
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The error text shown beside it.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) IsVisible = ErrorTexts.IdIn(Text) is not null;
    }

    /// <summary>Puts the ID on the window's clipboard.</summary>
    Task CopyAsync() =>
        ErrorTexts.IdIn(Text) is { } id && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? clipboard.SetTextAsync(id) : Task.CompletedTask;
}
