using OVS.Client.Input;

namespace OVS.Tests.Client.Input;

/// <summary>What each key action is: every action listed once, and which ones last while the key is down.</summary>
public class KeyActionTraitsTests
{
    [Fact]
    public void All_EveryAction_ListedOnceInEnumOrder() =>
        Assert.Equal([KeyAction.PushToTalk, KeyAction.LinkPushToTalk, KeyAction.PushToMute, KeyAction.ToggleMute, KeyAction.ToggleDeafen, KeyAction.ToggleStreamerMode], KeyActionTraits.All);

    [Fact]
    public void Holds_AllActions_ExactlyTalkLinkTalkAndMute() =>
        Assert.Equal([KeyAction.PushToTalk, KeyAction.LinkPushToTalk, KeyAction.PushToMute], KeyActionTraits.All.Where(KeyActionTraits.Holds.Contains));
}
