namespace OVS.Tests.TestSupport;

/// <summary>
/// A test class with a temporary folder of its own (prefix <paramref name="prefix"/>), deleted with everything in it
/// after each test. With <paramref name="quiet"/> an <see cref="IOException"/> while deleting is ignored (the UI tests,
/// where a file may still be open); without it, it fails the test.
/// </summary>
#pragma warning disable CA1515 // public: the base class of public xunit test classes
public abstract class TempDirectoryTest(string prefix, bool quiet) : IDisposable
#pragma warning restore CA1515
{
    protected readonly string dir = Directory.CreateTempSubdirectory(prefix).FullName;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Deletes the folder; a test class that adds clean-up overrides it and calls it last.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
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
