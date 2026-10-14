using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Client.Services;

/// <summary>Package 162: a desktop notification, and the chat tab a click on it opens.</summary>
/// <param name="Sender">Who it is about; at most one notification per sender in 5 s.</param>
/// <param name="Title">The notification's title.</param>
/// <param name="Text">The notification's text.</param>
/// <param name="Tab">Which chat tab a click opens.</param>
/// <param name="Partner">The partner's fingerprint of a private message, for its tab.</param>
internal sealed record DesktopNotice(string Sender, string Title, string Text, ChatTarget Tab, string? Partner = null);
