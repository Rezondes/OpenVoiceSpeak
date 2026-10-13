namespace OVS.Server.Voice;

/// <summary>A voice frame after the replay and loss tracking of its sender. A value type: it is made for every frame.</summary>
/// <param name="Kept">The frame to relay.</param>
/// <param name="Unseen">True when the frame number was not seen before.</param>
internal readonly record struct TrackedFrame(ReadOnlyMemory<byte> Kept, bool Unseen);
