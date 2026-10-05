using OVS.Client.Collections;
using OVS.Client.Models;

namespace OVS.Client.ViewModels;

/// <summary>One line of the opened page; IsTarget for the line a search hit opened.</summary>
public sealed class LogLineViewModel : IMotionKey
{
    /// <summary>Package 109: a line stays the same line while the filter changes; another page or file brings new ones.</summary>
    public object MotionKey => (Number, Text);

    public LogLineViewModel(int number, string text, string filter, bool isTarget)
    {
        Number = number;
        Text = text;
        IsTarget = isTarget;
        Segments = Split(text, filter);
    }

    public int Number { get; }
    public string Text { get; }
    public bool IsTarget { get; }
    public IReadOnlyList<LogSegment> Segments { get; }

    static List<LogSegment> Split(string text, string filter)
    {
        var segments = new List<LogSegment>();
        int at = 0;
        while (filter.Length > 0 && text.IndexOf(filter, at, StringComparison.OrdinalIgnoreCase) is var found and >= 0)
        {
            if (found > at) segments.Add(new(text[at..found], false));
            segments.Add(new(text.Substring(found, filter.Length), true));
            at = found + filter.Length;
        }
        if (at < text.Length || segments.Count == 0) segments.Add(new(text[at..], false));
        return segments;
    }
}
