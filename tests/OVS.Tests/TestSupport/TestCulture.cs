using System.Globalization;
using System.Runtime.CompilerServices;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 45 (A44): the tests expect the German texts, on every machine and on the English CI runner alike.
/// Tests for English set the culture of their own thread and restore it.
/// </summary>
static class TestCulture
{
    [ModuleInitializer]
    internal static void UseGerman()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
    }

    /// <summary>Runs the body with this UI culture on the current thread.</summary>
    public static T With<T>(string culture, Func<T> body)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }
}
