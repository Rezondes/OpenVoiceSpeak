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

    [Fact]
    public void ChordName_IsReadable()
    {
        Assert.Equal("Strg+Umschalt+M", new KeyChord(M, Ctrl | Shift).Name);
        Assert.Equal("Maustaste 4", new KeyChord(KeyPoller.VkXButton1).Name);
    }
}
