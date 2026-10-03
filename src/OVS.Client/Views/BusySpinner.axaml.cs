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

    /// <summary>
    /// Package 122: the spinners in the window that are visible themselves; a hidden one does not turn whatever is around
    /// it, so only these can be shown or hidden by an area around them. Usually none or a few, while every row has one.
    /// </summary>
    static readonly HashSet<BusySpinner> shown = [];

    /// <summary>Any visibility change may show or hide the spinners inside (a page, a card, a drawer).</summary>
    static BusySpinner() => IsVisibleProperty.Changed.AddClassHandler<Visual>((visual, _) =>
    {
        if (!visual.IsAttachedToVisualTree()) return;
        if (visual is BusySpinner self)
        {
            self.Track();
            self.Update(fade: true);
            return;
        }
        if (shown.Count == 0) return;
        foreach (var spinner in shown.Where(visual.IsVisualAncestorOf).ToList()) spinner.Update(fade: true); // updating may change the tree
    });

    public BusySpinner() => InitializeComponent();

    void Track()
    {
        if (IsVisible && this.IsAttachedToVisualTree()) shown.Add(this);
        else shown.Remove(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Track();
        Update(fade: false); // built or rebuilt (a row, a tab's content): it just shows (A117)
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        shown.Remove(this);
    }

    /// <param name="fade">
    /// Package 109: it or an area around it was shown: a spinner that comes into view this way fades in, also with the area
    /// that shows the wait (the logo waiting for the server, a card's action); not when it is built or rebuilt.
    /// </param>
    void Update(bool fade)
    {
        bool turning = this.GetSelfAndVisualAncestors().All(v => v.IsVisible);
        if (fade && turning && !Classes.Contains(TurningClass)) LiveMotion.FadeIn(this);
        Classes.Set(TurningClass, turning);
        this.FindAncestorOfType<Button>()?.Classes.Set(BusyClass, turning);
    }

    /// <summary>Package 108: when the spinner goes, the button's content fades back in its place (it fades in in <see cref="Update"/>).</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || !this.IsAttachedToVisualTree()) return;
        if (!change.GetNewValue<bool>()) LiveMotion.FadeIn(this.FindAncestorOfType<Button>()?.Presenter?.Child, 0.3);
    }
}
