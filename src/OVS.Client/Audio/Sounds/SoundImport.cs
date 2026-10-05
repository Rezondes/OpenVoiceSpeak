using System.Runtime.InteropServices;
using NAudio.Wave;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Localization;

namespace OVS.Client.Audio.Sounds;

/// <summary>
/// Package 48 (A47): turns a chosen WAV or MP3 into the client's own format (48 kHz mono) and keeps it in the
/// profile, so the original may be moved or deleted afterwards.
/// </summary>
public static class SoundImport
{
    public const int MaxSeconds = 5;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <returns>The file name inside the profile's sound folder, or why the file was refused.</returns>
    public static (string? File, string? Error) Prepare(string source, string profileDir, SoundEvent sound)
    {
        var info = new FileInfo(source);
        if (!info.Exists) return (null, Strings.SoundImport_Missing);
        if (info.Length > MaxFileBytes) return (null, Strings.SoundImport_TooBig);
        if (info.Extension.ToLowerInvariant() is not (".wav" or ".mp3")) return (null, Strings.SoundImport_Unreadable);

        float[] samples;
        try
        {
            using var reader = new AudioFileReader(source);
            if (reader.TotalTime > TimeSpan.FromSeconds(MaxSeconds + 0.05)) return (null, Strings.SoundImport_TooLong);
            int channels = reader.WaveFormat.Channels;
            var all = new List<float>();
            var buffer = new float[reader.WaveFormat.SampleRate * channels];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) all.AddRange(buffer.AsSpan(0, read));
            var mono = new float[all.Count / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += all[i * channels + c];
                mono[i] = sum / channels;
            }
            // Package 138: silence after the end pushes out what the resampler holds back (about 36 input samples plus
            // up to 1 ms), so 10 ms but at least 128 samples for very low rates
            int rate = reader.WaveFormat.SampleRate, length = (int)((long)mono.Length * AudioFormat.SampleRate / rate);
            var resampled = new SincResampler(rate, AudioFormat.SampleRate).Process([.. mono, .. new float[Math.Max(rate / 100, 128)]]);
            samples = resampled[..Math.Min(length, resampled.Length)].ToArray();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or COMException
                                   or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return (null, Strings.SoundImport_Unreadable);
        }
        if (samples.Length == 0) return (null, Strings.SoundImport_Unreadable);
        if (samples.Length > (MaxSeconds + 0.05) * AudioFormat.SampleRate) return (null, Strings.SoundImport_TooLong);

        var dir = Directory.CreateDirectory(Path.Combine(profileDir, SoundLibrary.Folder)).FullName;
        var name = $"{sound}-{Guid.NewGuid():N}.wav";
        using (var writer = new WaveFileWriter(Path.Combine(dir, name), WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 1)))
            writer.WriteSamples(samples, 0, samples.Length);
        return (name, null);
    }
}
