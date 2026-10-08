using OVS.Client.Input;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Input;

/// <summary>The text each key action is shown with, in the UI language.</summary>
public class KeyActionLabelsTests
{
    [Fact]
    public void Localize_EachAction_OwnTextInTheUiLanguage()
    {
        Assert.Equal("Push-to-Mute (stumm, solange gedrückt)", KeyActionLabels.Localize(KeyAction.PushToMute));
        var german = KeyActionTraits.All.Select(KeyActionLabels.Localize).ToList();
        var english = TestCulture.With("en-US", () => KeyActionTraits.All.Select(KeyActionLabels.Localize).ToList());
        Assert.Equal(KeyActionTraits.All.Count, german.Distinct().Count());
        Assert.Equal(KeyActionTraits.All.Count, english.Distinct().Count());
        Assert.DoesNotContain(german, text => KeyActionTraits.All.Any(a => a.ToString() == text)); // no enum name as a fallback
        Assert.NotEqual(german, english);
    }
}
