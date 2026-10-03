namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 125: a key state for <see cref="OVS.Client.Input.KeyPoller"/>, so keys go through the real bindings. Counts
/// the full sweeps of a key capture (only it asks for the last key code), so a test knows when a poll saw a change.
/// </summary>
public sealed class FakeKeys
{
    const int LastScanned = 0xFE;
    readonly HashSet<int> down = [];
    int sweeps;

    public bool IsDown(int vk)
    {
        if (vk == LastScanned) Interlocked.Increment(ref sweeps);
        lock (down) return down.Contains(vk);
    }

    public void Set(bool isDown, params int[] vks)
    {
        lock (down)
            foreach (int vk in vks)
                if (isDown) down.Add(vk);
                else down.Remove(vk);
    }

    /// <summary>Waits (at most 5 s) for two more capture sweeps: the second one surely started after the last change.</summary>
    public async Task NextSweep()
    {
        int target = Volatile.Read(ref sweeps) + 2;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref sweeps) < target && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
    }
}
