using OVS.Client.Settings;

namespace OVS.Client.Services;

/// <summary>Package 162: what the notification rule needs to know at the moment a message or a join arrives.</summary>
/// <param name="Settings">The switches of the three kinds.</param>
/// <param name="SelfId">The own session: own messages never notify.</param>
/// <param name="OwnNickname">The own nickname, for "@" mentions.</param>
/// <param name="WindowActive">The OVS window is in front: nothing notifies.</param>
internal sealed record NotificationState(ClientSettings Settings, uint SelfId, string OwnNickname, bool WindowActive);
