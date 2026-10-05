using OVS.Client.Localization;

namespace OVS.Client.Dialogs;

public sealed record BanChoice(string Reason, int? DurationMinutes, bool IncludeIp)
{
    public static IReadOnlyList<(string Label, int? Minutes)> Durations =>
        [(Strings.Ban_Hour, 60), (Strings.Ban_Day, 1440), (Strings.Ban_Week, 10080), (Strings.Ban_Permanent, null)];
}
