using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Logging;

namespace OVS.Client.Debug;

/// <summary>
/// --audio-debug: logs global PTT key changes and the sent frame rate once per second to the client log,
/// to check key detection with another window focused and the real microphone's frame rate.
/// </summary>
public sealed class AudioDebugLog : IDisposable
{
    readonly KeyPoller keys;
    readonly AudioEngine audio;
    readonly ClientLog log;
    readonly Timer timer;
    long lastFrames;
    float lastLevel = -120f;

    public AudioDebugLog(KeyPoller keys, AudioEngine audio, ClientLog log)
    {
        this.keys = keys;
        this.audio = audio;
        this.log = log;
        keys.Changed += OnKeys;
        audio.InputLevel += OnLevel;
        Write($"gestartet, PTT = {KeyPoller.KeyName(keys.PttKey)}, Link-PTT = {KeyPoller.KeyName(keys.LinkPttKey)}");
        timer = new Timer(_ => OnSecond(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    void OnKeys() => Write($"PTT {(keys.PttDown ? "gedrückt" : "losgelassen")}, Link-PTT {(keys.LinkPttDown ? "gedrückt" : "losgelassen")}");

    void OnLevel(float db) => lastLevel = db;

    void OnSecond()
    {
        long frames = audio.FramesSent;
        Write($"Frames gesendet: {frames - lastFrames}/s, Pegel {lastLevel:0} dBFS");
        lastFrames = frames;
    }

    void Write(string line) => log.Write("Audio-Debug: " + line);

    public void Dispose()
    {
        timer.Dispose();
        keys.Changed -= OnKeys;
        audio.InputLevel -= OnLevel;
    }
}
