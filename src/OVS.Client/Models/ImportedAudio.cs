namespace OVS.Client.Models;

/// <summary>
/// Package 171: the audio settings found in Discord or Fluxer, on their own scales. Null is a value the app did not have
/// or that could not be read; the mapping (ImportMapping) leaves it out.
/// </summary>
/// <param name="Source">"Discord" or "Fluxer", for the dialog's title.</param>
internal sealed record ImportedAudio(string Source)
{
    /// <summary>The device id both apps use for the system default.</summary>
    public const string DefaultDevice = "default";

    /// <summary>The microphone's id: a Windows endpoint id, or <see cref="DefaultDevice"/>.</summary>
    public string? InputDeviceId { get; init; }
    /// <summary>The speaker's id: a Windows endpoint id, or <see cref="DefaultDevice"/>.</summary>
    public string? OutputDeviceId { get; init; }
    /// <summary>True for voice activation, false for push-to-talk.</summary>
    public bool? VoiceActivation { get; init; }
    /// <summary>The voice activation sets its threshold itself.</summary>
    public bool? VadAutomatic { get; init; }
    /// <summary>The manual threshold in dB.</summary>
    public double? VadThresholdDb { get; init; }
    /// <summary>The microphone volume in percent, 100 unchanged.</summary>
    public double? InputPercent { get; init; }
    /// <summary>The speaker volume in percent, 100 unchanged.</summary>
    public double? OutputPercent { get; init; }
    /// <summary>How long push-to-talk keeps sending after the key is let go.</summary>
    public int? ReleaseDelayMs { get; init; }
    /// <summary>Any noise suppression is on.</summary>
    public bool? NoiseSuppression { get; init; }
    /// <summary>Echo cancellation is on.</summary>
    public bool? EchoCancellation { get; init; }
    /// <summary>Automatic gain is on.</summary>
    public bool? AutoGain { get; init; }
    /// <summary>The push-to-talk key as a Windows virtual key code, when it is a single keyboard key.</summary>
    public int? PushToTalkKey { get; init; }
}
