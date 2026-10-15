using Microsoft.Extensions.Logging;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;

namespace OVS.Client.Logging;

/// <summary>
/// Step 31 (N5): every line of the client log, in English, as one source-generated logger message each. The file holds
/// only the time stamp and the message (<see cref="ClientLog"/>); the level is for the reader of the code.
/// </summary>
static partial class ClientLogLines
{
    // Start and end (App)

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenVoiceSpeak client {Version} starting, profile {Profile}, options: {Options}")]
    public static partial void Starting(this ILogger logger, string version, string profile, string options);

    [LoggerMessage(Level = LogLevel.Information, Message = "New version started")]
    public static partial void NewVersionStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client stopped")]
    public static partial void Stopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Crash: {Exception}")]
    public static partial void Crashed(this ILogger logger, string exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved error: {Exception}")]
    public static partial void UnobservedError(this ILogger logger, string exception);

    /// <summary>Package 171: Discord's or Fluxer's audio settings could not be read.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Audio settings of Discord or Fluxer could not be read: {Problem}")]
    public static partial void AudioImportFailed(this ILogger logger, string problem);

    /// <summary>Package 170: ovs:// now points at this exe.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Server links (ovs://) registered for {Exe}")]
    public static partial void UrlProtocolRegistered(this ILogger logger, string exe);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server links (ovs://) could not be registered: {Error}")]
    public static partial void UrlProtocolFailed(this ILogger logger, string error);

    /// <summary>Package 170: a server link came in, at start or from a second start.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Server link received: {Address}")]
    public static partial void JoinLinkReceived(this ILogger logger, string address);

    /// <summary>What the removal of the exe replaced by the last update did (UpdateInstaller.CleanupOldAsync).</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Result}")]
    public static partial void OldVersionCleanup(this ILogger logger, string result);

    // Main window (MainViewModel)

    [LoggerMessage(Level = LogLevel.Information, Message = "Transmitting stopped")]
    public static partial void TransmitStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transmitting started: own channel")]
    public static partial void TransmitToChannel(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transmitting started: own channel and links")]
    public static partial void TransmitToChannelAndLinks(this ILogger logger);

    /// <summary>The switches are "on" or "off".</summary>
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Settings: mode {Mode}, keys {Keys}, input {Input}, output {Output}, gain {Gain:0.00}, volume {Volume:0.00}, " +
                  "VAD threshold {VadThreshold:0} dB, noise suppression {NoiseSuppression}, automatic gain {AutoGain}, " +
                  "echo cancellation {EchoCancellation}, content {Content}")]
    public static partial void SettingsApplied(this ILogger logger, TransmitMode mode, string keys, string input, string output, float gain, float volume,
        float vadThreshold, string noiseSuppression, string autoGain, string echoCancellation, AudioContent content);

    /// <summary>The kind is "CHANGED" or "new", the decision "accepted" or "refused".</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Server certificate {Kind} for {Host}:{Port}, fingerprint {Fingerprint}: {Decision}")]
    public static partial void ServerCertificate(this ILogger logger, string kind, string host, int port, string fingerprint, string decision);

    [LoggerMessage(Level = LogLevel.Information, Message = "Key: {Action}")]
    public static partial void KeyPressed(this ILogger logger, KeyAction action);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notice: {Text}")]
    public static partial void Notice(this ILogger logger, string text);

    // Audio

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Feature} off: {Library} cannot be loaded ({Error})")]
    public static partial void FilterOff(this ILogger logger, string feature, string library, string? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sound file {File} for {Sound} missing or unreadable, the default tone plays")]
    public static partial void SoundFileUnreadable(this ILogger logger, string file, SoundEvent sound);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Audio debug: started, keys: {Keys}")]
    public static partial void AudioDebugStarted(this ILogger logger, string keys);

    /// <summary>Each key is "down" or "up".</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "Audio debug: PTT {PushToTalk}, link PTT {LinkPushToTalk}, push-to-mute {PushToMute}")]
    public static partial void AudioDebugKeys(this ILogger logger, string pushToTalk, string linkPushToTalk, string pushToMute);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Audio debug: frames sent: {Frames}/s, level {Level:0} dBFS")]
    public static partial void AudioDebugSecond(this ILogger logger, long frames, float level);

    // Services

    [LoggerMessage(Level = LogLevel.Information, Message = "Bookmark saved")]
    public static partial void BookmarkSaved(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bookmark saved, password stored encrypted")]
    public static partial void BookmarkSavedWithPassword(this ILogger logger);

    /// <summary>The password is "stored" or "not stored"; replaced is empty or names the bookmark it replaces.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Bookmark '{Bookmark}' changed: {Name}, {Host}:{Port}, {Nickname}, password {Password}{Replaced}")]
    public static partial void BookmarkChanged(this ILogger logger, string bookmark, string name, string host, int port, string nickname, string password, string replaced);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bookmark '{Bookmark}' deleted")]
    public static partial void BookmarkDeleted(this ILogger logger, string bookmark);

    [LoggerMessage(Level = LogLevel.Information, Message = "Self test started")]
    public static partial void SelfTestStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Self test finished")]
    public static partial void SelfTestFinished(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update check failed: {Reason}")]
    public static partial void UpdateCheckFailed(this ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: no newer version")]
    public static partial void UpdateNone(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update available: {Version} ({Tag})")]
    public static partial void UpdateAvailable(this ILogger logger, string version, string tag);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installing update {Version}")]
    public static partial void UpdateInstalling(this ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Update failed: {Reason}")]
    public static partial void UpdateFailed(this ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "UDP now reachable")]
    public static partial void UdpNowReachable(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "UDP reachable again")]
    public static partial void UdpReachableAgain(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UDP not reachable: voice does not arrive (firewall or NAT?)")]
    public static partial void UdpNotReachable(this ILogger logger);

    // Connection (ConnectionViewModel)

    /// <summary>The status line of a failed connect as the window shows it, which names the failure itself.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Status}")]
    public static partial void ConnectFailed(this ILogger logger, string status);

    /// <summary>The detail follows in brackets with a leading space, or is empty.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Refused by the server: {Code}{Detail}")]
    public static partial void Refused(this ILogger logger, string code, string detail);

    /// <summary>The password note is " with password" or empty.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to {Host}:{Port} as {Nickname}{Password}")]
    public static partial void Connecting(this ILogger logger, string host, int port, string nickname, string password);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to '{Server}' as {Nickname} (session {Session}), channel '{Channel}', {Users} users online")]
    public static partial void Connected(this ILogger logger, string server, string? nickname, uint session, string? channel, int users);

    /// <summary>A request sent or a message received, as ProtocolLogText describes it.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    public static partial void Protocol(this ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected (own action)")]
    public static partial void DisconnectedOwnAction(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected by the server: {Reason} ({Detail})")]
    public static partial void DisconnectedByServer(this ILogger logger, string reason, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection lost, new attempts after {Delays} s")]
    public static partial void ConnectionLost(this ILogger logger, string delays);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnect: outdated attempt dropped")]
    public static partial void ReconnectOutdated(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnect: outdated attempt dropped ({Reason})")]
    public static partial void ReconnectOutdatedFailed(this ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconnect failed: {ExceptionType}: {Reason}")]
    public static partial void ReconnectFailed(this ILogger logger, string exceptionType, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconnect given up")]
    public static partial void ReconnectGivenUp(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconnect cancelled")]
    public static partial void ReconnectCancelled(this ILogger logger);
}
