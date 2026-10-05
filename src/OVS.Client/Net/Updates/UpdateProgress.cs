using OVS.Client.Localization;

namespace OVS.Client.Net.Updates;

/// <summary>Package 62: what the update card over the window shows. Total is null when GitHub sends no size.</summary>
public sealed record UpdateProgress(string Version, UpdatePhase Phase, long Bytes = 0, long? Total = null)
{
    const double MB = 1024 * 1024;

    public bool IsIndeterminate => Phase != UpdatePhase.Downloading || Total is not > 0;
    public double Percent => Total is > 0 and var total ? Math.Min(100, 100.0 * Bytes / total) : 0;
    public string Title => string.Format(Strings.Update_Title, Version);

    public string Detail => Phase switch
    {
        UpdatePhase.Verifying => Strings.Update_Verifying,
        UpdatePhase.Starting => Strings.Update_Starting,
        _ when Total is > 0 and var total => string.Format(Strings.Update_Size, Percent, Bytes / MB, total / MB),
        _ => string.Format(Strings.Update_SizeUnknown, Bytes / MB),
    };
}
