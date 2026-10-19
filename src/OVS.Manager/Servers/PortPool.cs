namespace OVS.Manager.Servers;

/// <summary>Package M7 (D2): the 100 ports 7000 to 7099, one per server for TCP and UDP.</summary>
internal static class PortPool
{
    /// <summary>The first port.</summary>
    public const int First = 7000;

    /// <summary>How many ports, so how many servers at most.</summary>
    public const int Count = 100;

    /// <summary>The lowest port not in <paramref name="taken"/>, null when all are taken (AC2).</summary>
    public static int? FindFree(IReadOnlyCollection<int> taken) =>
        Enumerable.Range(First, Count).Cast<int?>().FirstOrDefault(port => !taken.Contains(port!.Value));

    /// <summary>Whether <paramref name="port"/> may be taken next to <paramref name="taken"/>.</summary>
    public static PortProblem Check(int port, IReadOnlyCollection<int> taken) =>
        port is < First or >= First + Count ? PortProblem.OutOfRange : taken.Contains(port) ? PortProblem.Taken : PortProblem.None;
}
