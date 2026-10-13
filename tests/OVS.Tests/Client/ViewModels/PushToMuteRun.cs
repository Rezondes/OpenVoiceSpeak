using OVS.Client.Audio.Sounds;
using OVS.Shared.Protocol.Messages;

namespace OVS.Tests.Client.ViewModels;

/// <summary>What a push-to-mute run sent and which tones it played.</summary>
/// <param name="Sent">The requests sent to the server.</param>
/// <param name="Heard">The tones played.</param>
internal sealed record PushToMuteRun(List<Request> Sent, List<SoundEvent> Heard);
