using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OVS.Client.Animations;
using OVS.Client.Settings;

namespace OVS.Tests.TestSupport;

/// <summary>A list of <see cref="KeyedRow"/> entries with item motion, alone in an animated window, and its rows.</summary>
static class KeyedList
{
    /// <summary>A list of 30 px rows (or the controls <paramref name="row"/> makes), with <c>Enter</c> and <c>Flip</c> as given.</summary>
    public static ItemsControl Create(ObservableCollection<KeyedRow> rows, bool enter, bool flip, Func<KeyedRow, Control>? row = null)
    {
        var list = new ItemsControl { ItemsSource = rows, ItemTemplate = new FuncDataTemplate<KeyedRow>((r, _) => row?.Invoke(r) ?? new Border { Height = 30 }) };
        ItemMotion.SetEnter(list, enter);
        ItemMotion.SetFlip(list, flip);
        return list;
    }

    /// <summary>Shows <paramref name="list"/> in an animated window and waits until its first fill is over and it stands still.</summary>
    public static Window Show(ItemsControl list, int running)
    {
        var window = new Window { Width = 400, Height = 400, Content = list };
        window.Show();
        Motion.Apply(window, DisplayMode.Animated);
        Still(list, running);
        return window;
    }

    /// <summary>
    /// Frames until no more animations run than <paramref name="running"/> (read before the test acted) and
    /// <paramref name="list"/> no longer notes its places.
    /// </summary>
    public static void Still(ItemsControl list, int running)
    {
        MotionWait.Frame(); // at least one layout pass
        MotionWait.Until(() => AnimationCounter.Running <= running && !ItemMotion.TracksPlaces(list));
    }

    /// <summary>The row that shows the entry with <paramref name="key"/>.</summary>
    public static Control Container(ItemsControl list, string key) =>
        list.GetRealizedContainers().Single(c => c.DataContext is KeyedRow row && row.Key == key);
}
