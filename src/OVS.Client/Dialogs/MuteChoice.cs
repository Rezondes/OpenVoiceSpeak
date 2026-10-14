using OVS.Client.Localization;
using OVS.Client.Models;

namespace OVS.Client.Dialogs;

/// <summary>Package 166: how long a server mute lasts; null until it is lifted.</summary>
/// <param name="Minutes">The minutes until the mute ends by itself, null for no end.</param>
internal sealed record MuteChoice(int? Minutes)
{
    /// <summary>The value of "eigene Minuten" in <see cref="Durations"/>; the dialog then reads the minutes field.</summary>
    public const int OwnMinutes = -1;

    /// <summary>The longest timed mute the server takes, 28 days.</summary>
    public const int MaxMinutes = 28 * 24 * 60;

    /// <summary>The durations the mute dialog offers.</summary>
    public static IReadOnlyList<Choice<int?>> Durations =>
        [new(5, Strings.Mute_FiveMinutes), new(60, Strings.Ban_Hour), new(1440, Strings.Ban_Day), new(OwnMinutes, Strings.Mute_OwnMinutes), new(null, Strings.Mute_Unlimited)];
}
