using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Logging;

namespace OVS.Client.Diagnostics;

/// <summary>
/// --audio-debug: logs global PTT key changes and the sent frame rate once per second to the client log,
/// to check key detection with another window focused and the real microphone's frame rate.
/// </summary>
internal sealed class AudioDebugLog : IDisposable
{
    readonly KeyPoller keys;
    readonly AudioEngine audio;
    readonly ClientLog log;
    readonly Timer timer; // clock: debug
    long lastFrames;
    float lastLevel = -120f;

    /// <summary>Logs the keys and the frame rates once a second while the audio debug switch is on.</summary>
    public AudioDebugLog(KeyPoller keys, AudioEngine audio, ClientLog log)
    {
        this.keys = keys;
        this.audio = audio;
        this.log = log;
        keys.Changed += OnKeys;
        audio.InputLevel += OnLevel;
        log.AudioDebugStarted(keys.Bindings.Count == 0 ? "none" : string.Join(", ", keys.Bindings.Select(keyBinding => $"{keyBinding.Action} = {keyBinding.Chord.Name}")));
        timer = new Timer(_ => OnSecond(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)); // clock: debug
    }

    // Package 130: the keys themselves, without the release delay (Changed fires on the key's edges)
    void OnKeys() => log.AudioDebugKeys(DownUp(keys.IsDown(KeyAction.PushToTalk)), DownUp(keys.IsDown(KeyAction.LinkPushToTalk)), DownUp(keys.MuteHeld));

    static string DownUp(bool down) => down ? "down" : "up";

    void OnLevel(float db) => lastLevel = db;

    void OnSecond()
    {
        long frames = audio.FramesSent;
        log.AudioDebugSecond(frames - lastFrames, lastLevel);
        lastFrames = frames;
    }

    /// <summary>Stops logging.</summary>
    public void Dispose()
    {
        timer.Dispose();
        keys.Changed -= OnKeys;
        audio.InputLevel -= OnLevel;
    }
}
