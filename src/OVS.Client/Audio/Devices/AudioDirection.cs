namespace OVS.Client.Audio.Devices;

/// <summary>Which way a device carries sound: into the client (a microphone) or out of it (a speaker).</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum AudioDirection
#pragma warning restore CA1515
{
    /// <summary>A microphone.</summary>
    Input,
    /// <summary>A speaker.</summary>
    Output,
}
