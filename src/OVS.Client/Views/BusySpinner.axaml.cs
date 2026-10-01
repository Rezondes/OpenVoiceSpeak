using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace OVS.Client.Views;

/// <summary>Package 97 (A113): the small spinner of every waiting state (buttons, rows, list areas).</summary>
public partial class BusySpinner : UserControl
{
    /// <summary>Package 108: the class of a button while its spinner shows (Motion.axaml lets it shimmer).</summary>
    public const string BusyClass = "busy";

    /// <summary>Package 108 (A117): the spinner's class while it can be seen; only then does it turn.</summary>
    public const string TurningClass = "turning";

    /// <summary>Any visibility change may show or hide the spinners inside (a page, a card, a drawer).</summary>
    static BusySpinner() => IsVisibleProperty.Changed.AddClassHandler<Visual>((visual, _) =>
    {
        if (!visual.IsAttachedToVisualTree()) return;
        foreach (var spinner in visual.GetSelfAndVisualDescendants().OfType<BusySpinner>().ToList()) spinner.Update(); // updating may change the tree
    });

    public BusySpinner() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Update();
    }

    void Update()
    {
        bool turning = this.GetSelfAndVisualAncestors().All(v => v.IsVisible);
        Classes.Set(TurningClass, turning);
        this.FindAncestorOfType<Button>()?.Classes.Set(BusyClass, turning);
    }

    /// <summary>Package 108: the spinner fades in; when it goes, the button's content fades back in its place.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || !this.IsAttachedToVisualTree()) return;
        if (change.GetNewValue<bool>()) LiveMotion.FadeIn(this);
        else LiveMotion.FadeIn(this.FindAncestorOfType<Button>()?.Presenter?.Child, 0.3);
    }
}
