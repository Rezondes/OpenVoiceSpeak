using System.Globalization;

namespace OVS.Client.Localization;

public enum AppLanguage { System, German, English }

/// <summary>
/// Package 45 (A43): the client speaks German or English. "System" follows the Windows display language, anything
/// but German falls back to English. The choice is applied once at start, a change takes effect after a restart.
/// </summary>
public static class Language
{
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");
    static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public static CultureInfo Resolve(AppLanguage choice, CultureInfo windows) => choice switch
    {
        AppLanguage.German => German,
        AppLanguage.English => English,
        _ => windows.TwoLetterISOLanguageName == "de" ? German : English,
    };

    /// <summary>For the whole process: every thread, including the ones started later.</summary>
    public static void Apply(AppLanguage choice)
    {
        var culture = Resolve(choice, CultureInfo.CurrentUICulture);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
