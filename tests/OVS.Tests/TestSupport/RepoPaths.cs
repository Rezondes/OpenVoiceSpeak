namespace OVS.Tests.TestSupport;

/// <summary>Where the sources are: the tests that read source files find them above the test output.</summary>
static class RepoPaths
{
    /// <summary>The repository root: the first folder above the test output that holds <c>src/OVS.Client</c>.</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "OVS.Client"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }

    /// <summary>The client's source folder, <c>src/OVS.Client</c>.</summary>
    public static string ClientSource => Path.Combine(Root, "src", "OVS.Client");

    /// <summary>
    /// The files under <paramref name="dir"/> that match <paramref name="pattern"/>, without build output (a <c>bin</c> or
    /// <c>obj</c> folder at any depth) and generated <c>*.Designer.cs</c> files.
    /// </summary>
    public static IEnumerable<string> SourceFiles(string dir, string pattern) =>
        Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).Where(file =>
            !Path.GetRelativePath(dir, file).Split('/', '\\').Any(part => part is "bin" or "obj")
            && !file.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
}
