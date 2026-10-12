using System.Diagnostics.CodeAnalysis;
using OVS.Shared.Protocol;

namespace OVS.Client.Services;

/// <summary>
/// The own join in flight and the channel passwords that worked on this connection (Packages 94 and 139): which channel
/// the own "Betreten" asked for (arriving there is "entered", anywhere else "moved"), the password it was sent with, and
/// the passwords that let the user in, kept in memory for this connection only.
/// </summary>
internal sealed class ChannelJoin
{
    readonly Dictionary<Guid, string> passwords = [];
    Guid? pendingChannel;
    PendingPassword? pendingPassword;

    /// <summary>The own join to <paramref name="channelId"/> goes out, with the password entered or remembered (null without one).</summary>
    internal void Begin(Guid channelId, string? password)
    {
        pendingChannel = channelId;
        pendingPassword = password is null ? null : new PendingPassword(channelId, password);
    }

    /// <summary>Package 94: the server refused a request; a wrong or missing channel password forgets the one tried, so the next join asks again.</summary>
    internal void Refused(string? code)
    {
        if (code is ReasonCodes.WrongChannelPassword or ReasonCodes.ChannelPasswordRequired && pendingPassword is { } wrong)
        {
            passwords.Remove(wrong.Channel); // changed meanwhile, ask again next time
            pendingPassword = null;
        }
    }

    /// <summary>
    /// The own user arrived in <paramref name="channelId"/>: true when the own join went there ("entered"), false for a
    /// move. A password the own join was sent with to this channel worked and is remembered.
    /// </summary>
    internal bool Arrived(Guid channelId)
    {
        if (pendingPassword is { } worked && worked.Channel == channelId) passwords[worked.Channel] = worked.Password;
        pendingPassword = null;
        bool own = pendingChannel == channelId;
        pendingChannel = null;
        return own;
    }

    /// <summary>The password that worked for <paramref name="channelId"/> on this connection, if one did.</summary>
    internal bool TryGetPassword(Guid channelId, [NotNullWhen(true)] out string? password) => passwords.TryGetValue(channelId, out password);

    /// <summary>Package 139 (A148): the password that let the user into <paramref name="channelId"/> on this connection, or null.</summary>
    internal string? PasswordFor(Guid channelId) => passwords.GetValueOrDefault(channelId);

    /// <summary>The password the own join was sent with, and to which channel.</summary>
    sealed record PendingPassword(Guid Channel, string Password);
}
