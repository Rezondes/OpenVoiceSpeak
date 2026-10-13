using System.Globalization;
using OVS.Shared.Protocol;

namespace OVS.Client.Localization;

/// <summary>User texts for the protocol's error codes, in the chosen language (Package 45).</summary>
internal static class ErrorTexts
{
    /// <summary>
    /// A45: the server's own detail texts are German and meant for operators, so they only go to the client log.
    /// Kick and ban are different: their detail is the reason a person typed in.
    /// </summary>
    static readonly HashSet<string> DetailShown = [ReasonCodes.Kicked, ReasonCodes.Banned];

    static string? Text(string code) => Strings.ResourceManager.GetString("Error_" + code, Strings.Culture);

    /// <summary>True when the code has a text.</summary>
    public static bool Has(string code) => Text(code) is not null;

    /// <summary>The text of an error code in the UI language, with the server's detail where it helps.</summary>
    public static string For(string code, string? detail = null)
    {
        var text = Text(code) ?? TextFormat.Of(Strings.Error_Unknown, code);
        return string.IsNullOrWhiteSpace(detail) || !DetailShown.Contains(code) ? text : $"{text} ({detail})";
    }
}
