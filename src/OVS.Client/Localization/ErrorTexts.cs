using System.Globalization;
using System.Text.RegularExpressions;
using OVS.Shared.Protocol;

namespace OVS.Client.Localization;

/// <summary>User texts for the protocol's error codes, in the chosen language (Package 45).</summary>
internal static partial class ErrorTexts
{
    /// <summary>
    /// A45: the server's own detail texts are German and meant for operators, so they only go to the client log.
    /// Kick and ban are different: their detail is the reason a person typed in.
    /// </summary>
    static readonly HashSet<string> DetailShown = [ReasonCodes.Kicked, ReasonCodes.Banned];

    static string? Text(string code) => Strings.ResourceManager.GetString("Error_" + code, Strings.Culture);

    /// <summary>True when the code has a text.</summary>
    public static bool Has(string code) => Text(code) is not null;

    /// <summary>
    /// The text of an error code in the UI language, with the server's detail where it helps. Package 143 (A153): an
    /// unexpected server error's ID follows on a line of its own, so every place that shows the text shows the ID.
    /// </summary>
    public static string For(string code, string? detail = null, string? errorId = null)
    {
        var text = Text(code) ?? TextFormat.Of(Strings.Error_Unknown, code);
        if (!string.IsNullOrWhiteSpace(detail) && DetailShown.Contains(code)) text = $"{text} ({detail})";
        return errorId is null ? text : text + Environment.NewLine + TextFormat.Of(Strings.ErrorId_Line, errorId);
    }

    /// <summary>Package 143: the error ID in a text made by <see cref="For"/>, or null when it has none.</summary>
    public static string? IdIn(string? text) => text is null ? null : ErrorIdPattern().Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>The ID at the end of the last line, after a line break.</summary>
    [GeneratedRegex(@"\n[^\n]*?([0-9a-f]{32})$")]
    private static partial Regex ErrorIdPattern();
}
