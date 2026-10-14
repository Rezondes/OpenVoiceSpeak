using System.Net;
using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Server.Throttling;
using OVS.Server.Voice;
using OVS.Shared.Permissions;

namespace OVS.Server;

/// <summary>One connected, authenticated client.</summary>
internal sealed class Session(uint id, string fingerprint, string nickname, IPAddress ip, byte[] voiceKey, TimeProvider time, ServerMetrics? metrics = null)
    : IDisposable
{
    /// <summary>The session's id, also in its voice packets.</summary>
    public uint Id { get; } = id;
    /// <summary>The user's id.</summary>
    public string Fingerprint { get; } = fingerprint;
    /// <summary>The nickname of this login.</summary>
    public string Nickname { get; } = nickname;
    /// <summary>The client's address.</summary>
    public IPAddress Ip { get; } = ip;
    /// <summary>The session's voice key for UDP.</summary>
    public byte[] VoiceKey { get; } = voiceKey;

    /// <summary>The frames for the write loop; safe from any thread.</summary>
    public SessionOutbox Outbox { get; } = new(metrics);

    /// <summary>The request, list, costly and chat budgets (under the ServerState lock) and the voice flood rule (under the voice gate).</summary>
    public SessionThrottle Throttle { get; } = new(time);

    /// <summary>The voice state, under the voice gate of UdpVoiceServer (SessionVoice says what is read elsewhere).</summary>
    public SessionVoice Voice { get; } = new(voiceKey, time);

    /// <summary>Package 81: the log pages and searches, run on the thread pool; queued under the ServerState lock.</summary>
    public LogJobQueue LogJobs { get; } = new();

    /// <summary>Package 82: the prepared log download; its own lock, taken after the ServerState lock.</summary>
    public LogDownload LogDownload { get; } = new();

    // Guarded by the ServerState lock.
    /// <summary>Package 146: the code of the error sent for the request being handled, null while none was.</summary>
    public string? RequestResult { get; set; }
    /// <summary>The channel the user is in; guarded by the ServerState lock.</summary>
    public Guid ChannelId { get; set; }
    /// <summary>The user muted the own microphone.</summary>
    public bool SelfMuted { get; set; }
    /// <summary>The user deafened themselves.</summary>
    public bool SelfDeafened { get; set; }
    /// <summary>A moderator muted the user at the server.</summary>
    public bool ServerMuted { get; set; }
    /// <summary>Package 166: when a timed server mute ends by itself.</summary>
    public DateTimeOffset? ServerMutedUntil { get; set; }
    /// <summary>The user's rights, from the groups.</summary>
    public Permission Permissions { get; set; }
    /// <summary>The user's groups.</summary>
    public IReadOnlyList<Guid> GroupIds { get; set; } = [];
    /// <summary>Package 75: the backup upload in progress (one per session).</summary>
    public BackupUpload? Upload { get; set; }

    // ---- Package 70: statistics of this session, added to the UserRecord when it ends ----

    /// <summary>When the session started.</summary>
    public DateTimeOffset ConnectedAt { get; } = time.GetUtcNow();
    /// <summary>Guarded by the ServerState lock.</summary>
    public int ChatMessages { get; set; }
    /// <summary>Hash of the logo last sent to this session; guarded by the ServerState lock.</summary>
    public string? ServedIconHash { get; set; }
    long voiceFrames;

    /// <summary>Called by the UDP loop for each relayed voice packet; no lock on that path.</summary>
    public void CountVoiceFrame() => Interlocked.Increment(ref voiceFrames);

    /// <summary>20 ms per relayed voice packet.</summary>
    public TimeSpan SpeechTime => TimeSpan.FromMilliseconds(20 * Interlocked.Read(ref voiceFrames));

    /// <summary>Releases the voice state; The outbox stays usable: log jobs may still send after the end.</summary>
    public void Dispose() => Voice.Dispose();
}
