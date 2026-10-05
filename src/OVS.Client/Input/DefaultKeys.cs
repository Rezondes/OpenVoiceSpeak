namespace OVS.Client.Input;

/// <summary>Package 59: the push-to-talk key a new profile starts with.</summary>
public static class DefaultKeys
{
    /// <summary>Mouse 4 when there are side buttons, else Right Ctrl: rarely used by games and harmless while typing.</summary>
    public static KeyChord PushToTalk(int mouseButtons) => new(mouseButtons >= 5 ? KeyPoller.VkXButton1 : KeyPoller.VkRControl);
}
