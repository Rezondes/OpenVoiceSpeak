using OVS.Client.Localization;

namespace OVS.Client.Input;

/// <summary>The Windows virtual key codes the client knows by name, which of them are modifiers, and how a key is shown.</summary>
public static class VirtualKeys
{
    public const int XButton1 = 0x05;
    public const int XButton2 = 0x06;
    public const int RControl = 0xA3;
    internal const int Shift = 0x10, Control = 0x11;
    /// <summary>Win32 VK_MENU, the Alt key.</summary>
    internal const int Menu = 0x12;

    internal static bool IsModifier(int vk) => vk is Shift or Control or Menu || IsSidedModifier(vk);

    /// <summary>Left or right Shift, Ctrl, Alt: Windows reports these along with the plain modifier.</summary>
    internal static bool IsSidedModifier(int vk) => vk is >= 0xA0 and <= 0xA5;

    /// <summary>The key's name in the UI language.</summary>
    public static string Localize(int vk) => vk switch
    {
        0 => Strings.Key_None,
        XButton1 => Strings.Key_Mouse4,
        XButton2 => Strings.Key_Mouse5,
        0x04 => Strings.Key_MouseMiddle,
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        >= 0x30 and <= 0x5A => ((char)vk).ToString(),
        Shift => Strings.Key_Shift,
        Control => Strings.Key_Ctrl,
        Menu => "Alt",
        0x14 => Strings.Key_CapsLock,
        0x20 => Strings.Key_Space,
        0xA0 => Strings.Key_LeftShift,
        0xA1 => Strings.Key_RightShift,
        0xA2 => Strings.Key_LeftCtrl,
        RControl => Strings.Key_RightCtrl,
        0xA4 => Strings.Key_LeftAlt,
        0xA5 => Strings.Key_RightAlt,
        _ => string.Format(Strings.Key_Unknown, vk),
    };
}
