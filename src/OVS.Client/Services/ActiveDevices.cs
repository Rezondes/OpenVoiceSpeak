using OVS.Client.Audio.Devices;

namespace OVS.Client.Services;

/// <summary>The active microphones and speakers, as loaded together.</summary>
/// <param name="Inputs">The microphones.</param>
/// <param name="Outputs">The speakers.</param>
internal sealed record ActiveDevices(IReadOnlyList<AudioDevice> Inputs, IReadOnlyList<AudioDevice> Outputs);
