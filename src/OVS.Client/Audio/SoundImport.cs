using System.Runtime.InteropServices;
using NAudio.Wave;
using OVS.Client.Localization;
using OVS.Client.Settings;

namespace OVS.Client.Audio;

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
            samples = new LinearResampler(reader.WaveFormat.SampleRate, AudioFormat.SampleRate).Process(mono);
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

/// <summary>
/// The tones as the user set them up: an own file from the profile, otherwise the default tone. A file that has
/// gone missing falls back to the default and is logged once (A47).
/// </summary>
public sealed class SoundLibrary(string profileDir, Action<string> log)
{
    public const string Folder = "sounds";

    readonly Dictionary<string, float[]> cache = [];
    readonly HashSet<string> reported = [];

    string Dir => Path.Combine(profileDir, Folder);

    public float[] Samples(SoundEvent sound, SoundSetting setting)
    {
        if (setting.File is not { Length: > 0 } file) return SoundSynth.Render(sound);
        lock (cache)
        {
            if (cache.TryGetValue(file, out var done)) return done;
            try
            {
                // Only a bare name counts: settings.json must not point anywhere else on the disk.
                using var reader = new AudioFileReader(Path.Combine(Dir, Path.GetFileName(file)));
                var samples = new float[reader.Length / sizeof(float)];
                int read = reader.Read(samples, 0, samples.Length);
                return cache[file] = samples[..read];
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException or COMException
                                       or ArgumentException or UnauthorizedAccessException)
            {
                if (reported.Add(file)) log($"Sound-Datei {file} für {sound} fehlt oder ist unlesbar, der Standardton spielt");
                return SoundSynth.Render(sound);
            }
        }
    }

    /// <summary>After saving the settings: copies nobody points to any more (reset, replaced, cancelled imports).</summary>
    public void CleanUp(ClientSettings settings)
    {
        if (!Directory.Exists(Dir)) return;
        var keep = settings.Sounds.Values.Select(s => s.File).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(Dir))
        {
            if (keep.Contains(Path.GetFileName(path))) continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
