using Xunit.Sdk;

namespace OVS.Tests.TestSupport;

/// <summary>
/// A headless UI test too slow for every run: skipped unless OVS_SLOW_TESTS is set, like the website pictures with
/// OVS_SHOTS. It runs like an <c>[AvaloniaFact]</c> (same discoverer; that attribute is sealed). Run the slow tests
/// before a release or after changing what they guard:
/// <code>OVS_SLOW_TESTS=1 dotnet test tests/OVS.Tests</code>
/// </summary>
[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class SlowAvaloniaFactAttribute : FactAttribute
{
    public const string Variable = "OVS_SLOW_TESTS";

    public SlowAvaloniaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 }) Skip = $"Langsamer Test, nur mit {Variable}=1";
    }
}
