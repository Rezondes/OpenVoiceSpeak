using OVS.Client.Models;
using OVS.Client.Localization;

namespace OVS.Client.Dialogs;

/// <summary>Reason, duration (null = permanent) and whether the IP is banned too.</summary>
public sealed record BanChoice(string Reason, int? DurationMinutes, bool IncludeIp)
{
    /// <summary>The durations the ban dialog offers.</summary>
    public static IReadOnlyList<Choice<int?>> Durations =>
        [new(60, Strings.Ban_Hour), new(1440, Strings.Ban_Day), new(10080, Strings.Ban_Week), new(null, Strings.Ban_Permanent)];
}
