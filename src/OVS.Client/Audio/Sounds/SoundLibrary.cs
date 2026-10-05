using System.Runtime.InteropServices;
using NAudio.Wave;
using OVS.Client.Settings;

namespace OVS.Client.Audio.Sounds;

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
