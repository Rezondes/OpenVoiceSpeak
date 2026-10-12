namespace OVS.Client.Audio.Playback;

/// <summary>
/// One tick of the voice mixer: the frame and who is in it. A value type over buffers the mixer reuses on its next tick
/// (Package 124: nothing allocated per frame).
/// </summary>
/// <param name="Frame">The mixed frame, interleaved stereo (Package 142).</param>
/// <param name="Active">The speakers heard in this frame.</param>
public readonly record struct MixedFrame(float[] Frame, List<ActiveSpeaker> Active);
