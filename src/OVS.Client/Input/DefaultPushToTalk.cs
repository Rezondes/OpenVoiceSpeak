using OVS.Client.Interop;

namespace OVS.Client.Input;

/// <summary>Package 59: the push-to-talk key a new profile starts with.</summary>
public static class DefaultPushToTalk
{
    const int SmCMouseButtons = 43;

    /// <summary>Mouse 4 when there are side buttons, else Right Ctrl: rarely used by games and harmless while typing.</summary>
    public static KeyChord Choose(int mouseButtons) => new(mouseButtons >= 5 ? VirtualKeys.XButton1 : VirtualKeys.RControl);

    /// <summary>Package 59: how many buttons Windows knows the mouse has (0 without a mouse).</summary>
    public static int CountMouseButtons() => NativeMethods.GetSystemMetrics(SmCMouseButtons);
}
