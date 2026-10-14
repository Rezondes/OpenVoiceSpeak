using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OVS.Client.Animations;
using OVS.Client.Localization;

namespace OVS.Client.Controls;

/// <summary>
/// Package 153: the bar "Ungespeicherte Änderungen" with "Zurücksetzen" and "Speichern" as in Discord. It shows only while
/// something is changed, saving or refused (a key conflict); a way out with changes points at it (<see cref="Attention"/>):
/// it shakes in the animated display and stays lit until it hides.
/// </summary>
internal sealed class UnsavedBar : Border
{
    /// <summary>The page holds changes.</summary>
    public static readonly StyledProperty<bool> HasChangesProperty = AvaloniaProperty.Register<UnsavedBar, bool>(nameof(HasChanges));
    /// <summary>The save runs (Package 98: busy on the button).</summary>
    public static readonly StyledProperty<bool> IsSavingProperty = AvaloniaProperty.Register<UnsavedBar, bool>(nameof(IsSaving));
    /// <summary>What keeps the page from being saved, shown in the bar; Save is off meanwhile.</summary>
    public static readonly StyledProperty<string?> ErrorProperty = AvaloniaProperty.Register<UnsavedBar, string?>(nameof(Error));
    /// <summary>Package 157: a text of the page instead of "Ungespeicherte Änderungen" (the links' pending count).</summary>
    public static readonly StyledProperty<string?> NoteProperty = AvaloniaProperty.Register<UnsavedBar, string?>(nameof(Note));
    /// <summary>Saves and keeps the page open.</summary>
    public static readonly StyledProperty<ICommand?> SaveCommandProperty = AvaloniaProperty.Register<UnsavedBar, ICommand?>(nameof(SaveCommand));
    /// <summary>Puts every value back to what the page opened with.</summary>
    public static readonly StyledProperty<ICommand?> ResetCommandProperty = AvaloniaProperty.Register<UnsavedBar, ICommand?>(nameof(ResetCommand));

    /// <summary>The class that lights the bar after <see cref="Attention"/>.</summary>
    public const string AttentionClass = "attention";

    readonly TextBlock text = new() { Text = Strings.Unsaved_Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly Button save = new() { Name = "UnsavedSave", Classes = { "accent" }, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly Button reset = new() { Name = "UnsavedReset", Classes = { "subtle" }, Content = Strings.Unsaved_Reset, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly BusySpinner spinner = new() { IsVisible = false };

    /// <summary>The bar with its text and its two buttons; hidden until something changes.</summary>
    public UnsavedBar()
    {
        Classes.Add("unsavedBar");
        IsVisible = false;
        save.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { spinner, new TextBlock { Text = Strings.Dlg_Save } } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { reset, save } };
        DockPanel.SetDock(buttons, Dock.Right);
        Child = new DockPanel { Children = { buttons, text } };
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>The page holds changes.</summary>
    public bool HasChanges
    {
        get => GetValue(HasChangesProperty);
        set => SetValue(HasChangesProperty, value);
    }

    /// <summary>The save runs.</summary>
    public bool IsSaving
    {
        get => GetValue(IsSavingProperty);
        set => SetValue(IsSavingProperty, value);
    }

    /// <summary>What keeps the page from being saved.</summary>
    public string? Error
    {
        get => GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    /// <summary>A text of the page instead of the default one.</summary>
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    /// <summary>Saves and keeps the page open.</summary>
    public ICommand? SaveCommand
    {
        get => GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    /// <summary>Puts every value back.</summary>
    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    /// <summary>A way out with changes: the bar shakes (animated display) and stays lit until it hides.</summary>
    public void Attention()
    {
        if (!IsVisible) return;
        Classes.Add(AttentionClass);
        ShakeMotion.Shake(this);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SaveCommandProperty) save.Command = SaveCommand;
        else if (change.Property == ResetCommandProperty) reset.Command = ResetCommand;
        else if (change.Property == HasChangesProperty || change.Property == IsSavingProperty || change.Property == ErrorProperty || change.Property == NoteProperty) Update();
    }

    void Update()
    {
        IsVisible = HasChanges || IsSaving || Error is not null;
        if (!IsVisible) Classes.Remove(AttentionClass);
        text.Text = Error ?? Note ?? Strings.Unsaved_Text;
        text.Classes.Set("danger", Error is not null);
        spinner.IsVisible = IsSaving;
        save.IsEnabled = Error is null && !IsSaving;
        reset.IsEnabled = !IsSaving;
    }
}
