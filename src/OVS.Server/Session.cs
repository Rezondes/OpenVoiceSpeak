using System.Net;
using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Server.Throttling;
using OVS.Server.Voice;
using OVS.Shared.Permissions;

namespace OVS.Server;

/// <summary>One connected, authenticated client.</summary>
public sealed class Session(uint id, string fingerprint, string nickname, IPAddress ip, byte[] voiceKey, TimeProvider time)
    : IDisposable
{
    public uint Id { get; } = id;
    public string Fingerprint { get; } = fingerprint;
    public string Nickname { get; } = nickname;
    public IPAddress Ip { get; } = ip;
    public byte[] VoiceKey { get; } = voiceKey;

    /// <summary>The frames for the write loop; safe from any thread.</summary>
    public SessionOutbox Outbox { get; } = new();

    /// <summary>The request, list, costly and chat budgets (under the ServerState lock) and the voice flood rule (under the voice gate).</summary>
    public SessionThrottle Throttle { get; } = new(time);

    /// <summary>The voice state, under the voice gate of UdpVoiceServer (SessionVoice says what is read elsewhere).</summary>
    public SessionVoice Voice { get; } = new(voiceKey, time);

    /// <summary>Package 81: the log pages and searches, run on the thread pool; queued under the ServerState lock.</summary>
    public LogJobQueue LogJobs { get; } = new();

    /// <summary>Package 82: the prepared log download; its own lock, taken after the ServerState lock.</summary>
    public LogDownload LogDownload { get; } = new();

    // Guarded by the ServerState lock.
    public Guid ChannelId { get; set; }
    public bool SelfMuted { get; set; }
    public bool SelfDeafened { get; set; }
    public bool ServerMuted { get; set; }
    public Permission Permissions { get; set; }
    public IReadOnlyList<Guid> GroupIds { get; set; } = [];
    /// <summary>Package 75: the backup upload in progress (one per session).</summary>
    public BackupUpload? Upload { get; set; }

    // ---- Package 70: statistics of this session, added to the UserRecord when it ends ----

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

    public void Dispose() => Voice.Dispose(); // the outbox stays usable: log jobs may still send after the end
}
