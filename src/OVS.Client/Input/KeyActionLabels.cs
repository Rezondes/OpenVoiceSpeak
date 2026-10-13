using OVS.Client.Localization;

namespace OVS.Client.Input;

/// <summary>The text a key action is shown with, in the UI language.</summary>
internal static class KeyActionLabels
{
    /// <summary>The action's name in the UI language.</summary>
    public static string Localize(KeyAction action) => action switch
    {
        KeyAction.PushToTalk => Strings.KeyAction_PushToTalk,
        KeyAction.LinkPushToTalk => Strings.KeyAction_LinkPushToTalk,
        KeyAction.PushToMute => Strings.KeyAction_PushToMute,
        KeyAction.ToggleMute => Strings.KeyAction_ToggleMute,
        KeyAction.ToggleDeafen => Strings.KeyAction_ToggleDeafen,
        _ => action.ToString(),
    };
}
