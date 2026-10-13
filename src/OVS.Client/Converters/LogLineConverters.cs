using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OVS.Client.Converters;

/// <summary>Package 81: how a log line of the administration's log viewer shows.</summary>
internal static class LogLineConverters
{
    // Package 81: what the line filter found, readable on light and dark backgrounds
    static readonly IBrush MatchBrush = new ImmutableSolidColorBrush(Color.Parse("#66FFC83D"));

    /// <summary>Package 81: a log line as runs, the matches of the line filter highlighted.</summary>
    public static readonly IValueConverter LogSegments =
        new FuncValueConverter<IReadOnlyList<OVS.Client.Models.LogSegment>?, Avalonia.Controls.Documents.InlineCollection?>(segments =>
        {
            if (segments is null) return null;
            var inlines = new Avalonia.Controls.Documents.InlineCollection();
            foreach (var logSegment in segments)
                inlines.Add(logSegment.IsMatch ? new Avalonia.Controls.Documents.Run(logSegment.Text) { Background = MatchBrush, FontWeight = FontWeight.SemiBold }
                    : new Avalonia.Controls.Documents.Run(logSegment.Text));
            return inlines;
        });
}
