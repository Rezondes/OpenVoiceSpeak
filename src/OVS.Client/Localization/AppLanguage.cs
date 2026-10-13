namespace OVS.Client.Localization;

/// <summary>The language the user chose.</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum AppLanguage
#pragma warning restore CA1515
{
    /// <summary>The language of Windows.</summary>
    System,
    /// <summary>German.</summary>
    German,
    /// <summary>English.</summary>
    English,
}
