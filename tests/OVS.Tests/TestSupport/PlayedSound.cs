namespace OVS.Tests.TestSupport;

/// <summary>Package 174: one tone handed over to a <see cref="FakeSpeaker"/> that plays the sounds itself.</summary>
internal sealed record PlayedSound(float[] Samples, float Gain, int Offset, bool Restart);
