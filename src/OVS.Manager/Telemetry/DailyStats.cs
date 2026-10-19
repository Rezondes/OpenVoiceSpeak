namespace OVS.Manager.Telemetry;

/// <summary>Package M21 (AC3, MA7, A204): a server's day in its own time zone; never deleted.</summary>
internal sealed class DailyStats
{
    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The day in the server's time zone.</summary>
    public DateOnly Day { get; set; }

    /// <summary>The sessions admitted (<c>ovs.connections</c>).</summary>
    public long Connections { get; set; }

    /// <summary>The most different users of the day (<c>ovs.users.daily</c>, PLAN 192).</summary>
    public long Users { get; set; }

    /// <summary>The most users first seen that day (<c>ovs.users.daily.new</c>).</summary>
    public long NewUsers { get; set; }

    /// <summary>The voice and control bytes in.</summary>
    public long BytesIn { get; set; }

    /// <summary>The voice and control bytes out.</summary>
    public long BytesOut { get; set; }
}
