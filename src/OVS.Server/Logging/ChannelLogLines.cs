using Microsoft.Extensions.Logging;

namespace OVS.Server.Logging;

/// <summary>
/// Step 31 (N5): every line of a channel's log, in English, as one source-generated logger message each. The logger
/// is the channel's own (<see cref="ServerLogWriter.Channel"/>), which puts the channel's name in brackets in front.
/// </summary>
static partial class ChannelLogLines
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} joined the channel (connected)")]
    public static partial void JoinedOnConnect(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} joined the channel (coming from {Channel})")]
    public static partial void JoinedFrom(this ILogger logger, string nickname, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} left the channel (moving to {Channel})")]
    public static partial void LeftTo(this ILogger logger, string nickname, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} left the channel ({Reason})")]
    public static partial void Left(this ILogger logger, string nickname, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} left the channel (channel deleted)")]
    public static partial void LeftDeleted(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} joined the channel (channel {Channel} was deleted)")]
    public static partial void JoinedFromDeleted(this ILogger logger, string nickname, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} was moved by {Moderator} to {Channel}")]
    public static partial void MovedOut(this ILogger logger, string nickname, string moderator, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname} was moved here by {Moderator} from {Channel}")]
    public static partial void MovedIn(this ILogger logger, string nickname, string moderator, string channel);

    /// <summary>The options follow in brackets with a leading space, or are empty.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Channel created by {Nickname}{Options}")]
    public static partial void Created(this ILogger logger, string nickname, string options);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel changed by {Nickname}: {Changes}")]
    public static partial void Changed(this ILogger logger, string nickname, string changes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Channel deleted by {Nickname}")]
    public static partial void Deleted(this ILogger logger, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Group '{Group}' removed from the group lock (group deleted)")]
    public static partial void GroupLockRemoved(this ILogger logger, string group);

    [LoggerMessage(Level = LogLevel.Information, Message = "Link to {Channel} set by {Nickname}")]
    public static partial void LinkSet(this ILogger logger, string channel, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Link to {Channel} removed by {Nickname}")]
    public static partial void LinkRemoved(this ILogger logger, string channel, string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "Link to {Channel} removed (channel deleted)")]
    public static partial void LinkRemovedWithChannel(this ILogger logger, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nickname}: {Text}")]
    public static partial void Chat(this ILogger logger, string nickname, string text);
}
