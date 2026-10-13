using System.Globalization;
using OVS.Client.Localization;

namespace OVS.Client.Net.Updates;

/// <summary>Package 62: what the update card over the window shows. Total is null when GitHub sends no size.</summary>
internal sealed record UpdateProgress(string Version, UpdatePhase Phase, long Bytes = 0, long? Total = null)
{
    const double MB = 1024 * 1024;

    /// <summary>No percentage can be shown.</summary>
    public bool IsIndeterminate => Phase != UpdatePhase.Downloading || Total is not > 0;
    /// <summary>How much is loaded, 0 to 100.</summary>
    public double Percent => Total is > 0 and var total ? Math.Min(100, 100.0 * Bytes / total) : 0;
    /// <summary>The card's title.</summary>
    public string Title => TextFormat.Of(Strings.Update_Title, Version);

    /// <summary>The card's detail line.</summary>
    public string Detail => Phase switch
    {
        UpdatePhase.Verifying => Strings.Update_Verifying,
        UpdatePhase.Starting => Strings.Update_Starting,
        _ when Total is > 0 and var total => TextFormat.Of(Strings.Update_Size, Percent, Bytes / MB, total / MB),
        _ => TextFormat.Of(Strings.Update_SizeUnknown, Bytes / MB),
    };
}
