namespace OVS.Tests.Website;

/// <summary>
/// Package 116 (A124): the four looks (de/en, light/dark); skipped without OVS_SHOTS, so the normal test run and CI never
/// render the website's images.
/// </summary>
internal sealed class ShotVariantsAttribute : Xunit.Sdk.DataAttribute
{
    public ShotVariantsAttribute()
    {
        if (ShotWriter.Folder is null) Skip = "Website-Bilder nur mit OVS_SHOTS=<Ordner>";
    }

    public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod)
    {
        foreach (var lang in new[] { "de", "en" })
            foreach (var theme in new[] { "light", "dark" })
                yield return [lang, theme];
    }
}
