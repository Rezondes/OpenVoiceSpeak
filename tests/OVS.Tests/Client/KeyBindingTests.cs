using OVS.Client.Input;
using static OVS.Client.Input.ChordModifiers;

namespace OVS.Tests.Client;

/// <summary>Package 29: which actions a set of pressed keys triggers.</summary>
public class KeyBindingTests
{
    const int F1 = 0x70, M = 0x4D;

    static KeyBinding Bind(KeyAction action, int key, ChordModifiers modifiers = None) => new(action, new KeyChord(key, modifiers));

    static HashSet<KeyAction> Resolve(KeyBinding[] bindings, int[] down, ChordModifiers held) =>
        KeyActions.Resolve(bindings, down.Contains, held);

    /// <summary>Package 59: a new profile can talk right away; mouse 4 if the mouse has side buttons, else Right Ctrl.</summary>
    [Fact]
    public void DefaultPtt_Mouse4WithSideButtons_ElseRightCtrl()
    {
        Assert.Equal(new KeyChord(KeyPoller.VkXButton1), DefaultKeys.PushToTalk(mouseButtons: 5));
        Assert.Equal(new KeyChord(KeyPoller.VkXButton1), DefaultKeys.PushToTalk(mouseButtons: 7));
        Assert.Equal(new KeyChord(KeyPoller.VkRControl), DefaultKeys.PushToTalk(mouseButtons: 3));
        Assert.Equal(new KeyChord(KeyPoller.VkRControl), DefaultKeys.PushToTalk(mouseButtons: 0)); // no mouse at all
        Assert.InRange(KeyPoller.MouseButtonCount(), 0, 32);
    }

    [Fact]
    public void RightCtrl_NameAndResolves()
    {
        Assert.Equal("Strg rechts", new KeyChord(KeyPoller.VkRControl).Name);
        KeyBinding[] bindings = [Bind(KeyAction.PushToTalk, KeyPoller.VkRControl)];
        Assert.Equal([KeyAction.PushToTalk], Resolve(bindings, [KeyPoller.VkRControl], Ctrl)); // Windows reports Ctrl as held, too
    }

    [Fact]
    public void Resolve_ChordWithMoreModifiersWins()
    {
        KeyBinding[] bindings = [Bind(KeyAction.ToggleMute, F1, Ctrl), Bind(KeyAction.PushToTalk, F1)];
        Assert.Equal([KeyAction.ToggleMute], Resolve(bindings, [F1], Ctrl));
        Assert.Equal([KeyAction.PushToTalk], Resolve(bindings, [F1], None));
    }

    [Fact]
    public void Resolve_RequiresAllModifiers_ExtraOnesDoNotHurt()
    {
        KeyBinding[] bindings = [Bind(KeyAction.ToggleDeafen, M, Ctrl | Shift), Bind(KeyAction.PushToTalk, KeyPoller.VkXButton1)];
        Assert.Empty(Resolve(bindings, [M], Ctrl));
        Assert.Equal([KeyAction.ToggleDeafen], Resolve(bindings, [M], Ctrl | Shift));
        // Shift held while running in a game: the plain PTT key still works.
        Assert.Equal([KeyAction.PushToTalk], Resolve(bindings, [KeyPoller.VkXButton1], Shift));
        Assert.Empty(Resolve(bindings, [], Ctrl | Shift | Alt));
    }

    [Fact]
    public void Toggle_FiresOncePerPress_HoldReportsChanges()
    {
        var tracker = new KeyStateTracker();
        var held = new HashSet<KeyAction> { KeyAction.ToggleMute };
        var presses = Enumerable.Range(0, 10).Sum(_ => tracker.Update(held).Pressed.Count);
        Assert.Equal(1, presses); // a held toggle key fires once, not every 10 ms poll
        Assert.Empty(tracker.Update(new HashSet<KeyAction>()).Pressed);
        Assert.Single(tracker.Update(held).Pressed); // released and pressed again: fires again

        Assert.True(tracker.Update(new HashSet<KeyAction> { KeyAction.ToggleMute, KeyAction.PushToTalk }).HoldChanged);
        Assert.False(tracker.Update(new HashSet<KeyAction> { KeyAction.ToggleMute, KeyAction.PushToTalk }).HoldChanged);
    }

    /// <summary>Package 41: one action on two keys: either holds it, it ends when both are up.</summary>
    [Fact]
    public void SameActionTwoKeys_EitherHolds_EndsWhenBothUp()
    {
        KeyBinding[] bindings = [Bind(KeyAction.PushToMute, KeyPoller.VkXButton1), Bind(KeyAction.PushToMute, KeyPoller.VkXButton2)];
        var tracker = new KeyStateTracker();
        (bool Active, bool Changed) Poll(params int[] down)
        {
            var active = Resolve(bindings, down, None);
            return (active.Contains(KeyAction.PushToMute), tracker.Update(active).HoldChanged);
        }
        Assert.Equal((true, true), Poll(KeyPoller.VkXButton1));
        Assert.Equal((true, false), Poll(KeyPoller.VkXButton1, KeyPoller.VkXButton2));
        Assert.Equal((true, false), Poll(KeyPoller.VkXButton2)); // the first let go, the second still holds
        Assert.Equal((false, true), Poll());
    }

    [Fact]
    public void ChordName_IsReadable()
    {
        Assert.Equal("Strg+Umschalt+M", new KeyChord(M, Ctrl | Shift).Name);
        Assert.Equal("Maustaste 4", new KeyChord(KeyPoller.VkXButton1).Name);
    }
}
