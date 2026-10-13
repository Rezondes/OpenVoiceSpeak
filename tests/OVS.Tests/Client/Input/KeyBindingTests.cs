using OVS.Client.Input;
using OVS.Tests.TestSupport;
using static OVS.Client.Input.ChordModifiers;

namespace OVS.Tests.Client.Input;

/// <summary>Package 29: which actions a set of pressed keys triggers.</summary>
public class KeyBindingTests
{
    const int F1 = 0x70, KeyM = 0x4D;

    static KeyBinding Bind(KeyAction action, int key, ChordModifiers modifiers = None) => new(action, new KeyChord(key, modifiers));

    static HashSet<KeyAction> Resolve(KeyBinding[] bindings, int[] down, ChordModifiers held) =>
        KeyActionTraits.All.Where(KeyBindingMatcher.Resolve(bindings, down.Contains, held).Contains).ToHashSet();

    static KeyActionSet Set(params KeyAction[] actions) => actions.Aggregate(new KeyActionSet(), (set, action) => set.With(action));

    /// <summary>Package 59: a new profile can talk right away; mouse 4 if the mouse has side buttons, else Right Ctrl.</summary>
    [Fact]
    public void DefaultPtt_Mouse4WithSideButtons_ElseRightCtrl()
    {
        Assert.Equal(new KeyChord(VirtualKeys.XButton1), DefaultPushToTalk.Choose(mouseButtons: 5));
        Assert.Equal(new KeyChord(VirtualKeys.XButton1), DefaultPushToTalk.Choose(mouseButtons: 7));
        Assert.Equal(new KeyChord(VirtualKeys.RControl), DefaultPushToTalk.Choose(mouseButtons: 3));
        Assert.Equal(new KeyChord(VirtualKeys.RControl), DefaultPushToTalk.Choose(mouseButtons: 0)); // no mouse at all
        Assert.InRange(DefaultPushToTalk.CountMouseButtons(), 0, 32);
    }

    [Fact]
    public void RightCtrl_Bound_NamedAndResolved()
    {
        Assert.Equal("Strg rechts", new KeyChord(VirtualKeys.RControl).Name);
        KeyBinding[] bindings = [Bind(KeyAction.PushToTalk, VirtualKeys.RControl)];
        Assert.Equal([KeyAction.PushToTalk], Resolve(bindings, [VirtualKeys.RControl], Ctrl)); // Windows reports Ctrl as held, too
    }

    /// <summary>A binding without a key (the dialog's "no key yet") never fires, even if the poll reports key 0 down.</summary>
    [Fact]
    public void Resolve_BindingWithoutKey_NeverActive()
    {
        KeyBinding[] bindings = [Bind(KeyAction.PushToTalk, 0), Bind(KeyAction.ToggleMute, F1)];
        Assert.Equal([KeyAction.ToggleMute], Resolve(bindings, [0, F1], None));
    }

    [Fact]
    public void Resolve_TwoChordsMatch_MoreModifiersWin()
    {
        KeyBinding[] bindings = [Bind(KeyAction.ToggleMute, F1, Ctrl), Bind(KeyAction.PushToTalk, F1)];
        Assert.Equal([KeyAction.ToggleMute], Resolve(bindings, [F1], Ctrl));
        Assert.Equal([KeyAction.PushToTalk], Resolve(bindings, [F1], None));
    }

    [Fact]
    public void Resolve_RequiresAllModifiers_ExtraOnesDoNotHurt()
    {
        KeyBinding[] bindings = [Bind(KeyAction.ToggleDeafen, KeyM, Ctrl | Shift), Bind(KeyAction.PushToTalk, VirtualKeys.XButton1)];
        Assert.Empty(Resolve(bindings, [KeyM], Ctrl));
        Assert.Equal([KeyAction.ToggleDeafen], Resolve(bindings, [KeyM], Ctrl | Shift));
        // Shift held while running in a game: the plain PTT key still works.
        Assert.Equal([KeyAction.PushToTalk], Resolve(bindings, [VirtualKeys.XButton1], Shift));
        Assert.Empty(Resolve(bindings, [], Ctrl | Shift | Alt));
    }

    [Fact]
    public void Toggle_FiresOncePerPress_HoldReportsChanges()
    {
        var tracker = new KeyStateTracker();
        var held = Set(KeyAction.ToggleMute);
        var presses = Enumerable.Range(0, 10).Sum(_ => tracker.Update(held).Pressed.Count);
        Assert.Equal(1, presses); // a held toggle key fires once, not every 10 ms poll
        Assert.Equal(0, tracker.Update(Set()).Pressed.Count);
        Assert.Equal(Set(KeyAction.ToggleMute), tracker.Update(held).Pressed); // released and pressed again: fires again

        Assert.True(tracker.Update(Set(KeyAction.ToggleMute, KeyAction.PushToTalk)).HoldChanged);
        Assert.False(tracker.Update(Set(KeyAction.ToggleMute, KeyAction.PushToTalk)).HoldChanged);
    }

    /// <summary>Package 41: one action on two keys: either holds it, it ends when both are up.</summary>
    [Fact]
    public void SameActionTwoKeys_EitherHolds_EndsWhenBothUp()
    {
        KeyBinding[] bindings = [Bind(KeyAction.PushToMute, VirtualKeys.XButton1), Bind(KeyAction.PushToMute, VirtualKeys.XButton2)];
        var tracker = new KeyStateTracker();
        (bool Active, bool Changed) Poll(params int[] down)
        {
            var active = KeyBindingMatcher.Resolve(bindings, down.Contains, None);
            return (active.Contains(KeyAction.PushToMute), tracker.Update(active).HoldChanged);
        }
        Assert.Equal((true, true), Poll(VirtualKeys.XButton1));
        Assert.Equal((true, false), Poll(VirtualKeys.XButton1, VirtualKeys.XButton2));
        Assert.Equal((true, false), Poll(VirtualKeys.XButton2)); // the first let go, the second still holds
        Assert.Equal((false, true), Poll());
    }

    /// <summary>
    /// Package 125: Shift, Ctrl or Alt alone becomes the key when it is let go without another key meanwhile
    /// (Windows reports the side and the plain modifier down together). With another key it stays a modifier.
    /// </summary>
    [Fact]
    public async Task Capture_SingleModifier_OnRelease()
    {
        const int Shift_ = VirtualKeys.Shift, LShift = 0xA0, Ctrl_ = VirtualKeys.Control, RCtrl = VirtualKeys.RControl;
        var fake = new FakeKeys();
        using var keys = new KeyPoller(TimeProvider.System, fake.IsDown);
        async Task<KeyChord?> CaptureAsync(params (bool Down, int[] Keys)[] steps)
        {
            var chord = keys.CaptureNextChordAsync();
            await fake.NextSweepAsync(); // the first sweep only records what is already down
            foreach (var (isDown, vks) in steps)
            {
                Assert.False(chord.IsCompleted); // not while a modifier is still down
                fake.Set(isDown, vks);
                await fake.NextSweepAsync(() => chord.IsCompleted);
            }
            return chord.IsCompleted ? await chord : null;
        }

        Assert.Equal(new KeyChord(LShift), await CaptureAsync((true, [Shift_, LShift]), (false, [Shift_, LShift])));
        Assert.Equal(new KeyChord(F1, Shift), await CaptureAsync((true, [Shift_, LShift]), (true, [F1])));
        fake.Set(false, Shift_, LShift, F1);
        // Shift and Ctrl together are no single key; once both are up, Ctrl alone counts again
        Assert.Equal(new KeyChord(RCtrl), await CaptureAsync((true, [Shift_, LShift]), (true, [Ctrl_, RCtrl]), (false, [Ctrl_, RCtrl]),
            (false, [Shift_, LShift]), (true, [Ctrl_, RCtrl]), (false, [Ctrl_, RCtrl])));
    }

    /// <summary>Package 125: a chord of a modifier alone resolves like any key, also with the plain modifier reported held.</summary>
    [Fact]
    public void Resolve_SingleModifierChord_ResolvesLikeAnyKey()
    {
        const int LShift = 0xA0, LAlt = 0xA4;
        KeyBinding[] bindings = [Bind(KeyAction.PushToMute, LShift), Bind(KeyAction.ToggleMute, LAlt)];
        Assert.Equal([KeyAction.PushToMute], Resolve(bindings, [LShift], Shift));
        Assert.Equal([KeyAction.ToggleMute], Resolve(bindings, [LAlt], Alt));
        Assert.Empty(Resolve(bindings, [0xA1], Shift)); // the right Shift is another key
        Assert.Equal("Umschalt links", new KeyChord(LShift).Name);
    }

    [Fact]
    public void ChordName_Chord_Readable()
    {
        Assert.Equal("Strg+Umschalt+M", new KeyChord(KeyM, Ctrl | Shift).Name);
        Assert.Equal("Maustaste 4", new KeyChord(VirtualKeys.XButton1).Name);
    }
}
