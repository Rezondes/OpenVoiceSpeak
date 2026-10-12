using OVS.Client.Models;
using OVS.Client.Localization;

namespace OVS.Client.Dialogs;

public sealed record BanChoice(string Reason, int? DurationMinutes, bool IncludeIp)
{
    public static IReadOnlyList<Choice<int?>> Durations =>
        [new(60, Strings.Ban_Hour), new(1440, Strings.Ban_Day), new(10080, Strings.Ban_Week), new(null, Strings.Ban_Permanent)];
}
