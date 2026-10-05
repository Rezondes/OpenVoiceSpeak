namespace OVS.Tests.TestSupport;

/// <summary>
/// A test class with a temporary folder of its own (prefix <paramref name="prefix"/>), deleted with everything in it
/// after each test. With <paramref name="quiet"/> an <see cref="IOException"/> while deleting is ignored (the UI tests,
/// where a file may still be open); without it, it fails the test.
/// </summary>
public abstract class TempDirectoryTest(string prefix, bool quiet) : IDisposable
{
    protected readonly string dir = Directory.CreateTempSubdirectory(prefix).FullName;

    public virtual void Dispose()
    {
        if (!quiet)
        {
            Directory.Delete(dir, true);
            return;
        }
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }
}
