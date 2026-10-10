namespace OVS.Tests.TestSupport;

/// <summary>What <see cref="EngineProbe.ReadEngineSwitches"/> read.</summary>
sealed record EngineSwitches(bool Connected, bool SelfMuted, bool Deafened, bool HasSpeakLinked, bool ChannelMuted);
