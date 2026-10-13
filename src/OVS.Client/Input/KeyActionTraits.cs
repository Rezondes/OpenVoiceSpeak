namespace OVS.Client.Input;

/// <summary>What the key actions are: every action, and which of them are hold actions (they last while the key is down).</summary>
internal static class KeyActionTraits
{
    /// <summary>Every action.</summary>
    public static readonly IReadOnlyList<KeyAction> All = Enum.GetValues<KeyAction>();

    /// <summary>The hold actions as one set: push-to-talk, link push-to-talk and push-to-mute.</summary>
    public static readonly KeyActionSet Holds = new KeyActionSet().With(KeyAction.PushToTalk).With(KeyAction.LinkPushToTalk).With(KeyAction.PushToMute);
}
