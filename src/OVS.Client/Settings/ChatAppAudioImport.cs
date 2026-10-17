using System.Text;
using System.Text.Json;
using OVS.Client.Models;

namespace OVS.Client.Settings;

/// <summary>
/// Package 171 (A174, A175): reads the audio settings of a chat app built on Electron from the local storage of its web
/// view under %APPDATA% (a LevelDB folder, read only through LevelDbReader). Each app (Discord, Fluxer) says where its
/// folders are and how its JSON reads; what this base does is shared: finding the folder, reading the storage, decoding
/// a value and reading JSON values. The format is undocumented, so this is best effort: what an app does not recognise
/// stays null.
/// </summary>
internal abstract class ChatAppAudioImport
{
    /// <summary>The apps in the order of their buttons (Package 187): Discord, then Fluxer. A new app needs only its class here.</summary>
    public static IReadOnlyList<ChatAppAudioImport> All { get; } = [new DiscordAudioImport(), new FluxerAudioImport()];

    /// <summary>The app's name, for the dialog's title.</summary>
    public abstract string Name { get; }

    /// <summary>The app's folders under %APPDATA%, e.g. its PTB and Canary builds.</summary>
    protected abstract IReadOnlyList<string> Folders { get; }

    /// <summary>The app's audio settings in its local storage; null when it has none.</summary>
    /// <exception cref="JsonException">A value is no JSON.</exception>
    protected abstract ImportedAudio? FromStorage(IReadOnlyDictionary<string, byte[]> storage);

    /// <summary>One of the app's folders exists for this Windows user.</summary>
    /// <param name="appData">The roaming application data folder.</param>
    public bool IsInstalled(string appData) => Folders.Any(folder => Directory.Exists(StorageOf(appData, folder)));

    /// <summary>The settings of the app's first folder that has some, or why none were read.</summary>
    /// <param name="appData">The roaming application data folder.</param>
    public AudioImportResult Read(string appData)
    {
        string? problem = null;
        foreach (var folder in Folders.Where(folder => Directory.Exists(StorageOf(appData, folder))))
        {
            try
            {
                if (FromStorage(LevelDbReader.Read(StorageOf(appData, folder))) is { } audio) return new AudioImportResult(audio, null);
                problem ??= $"{folder}: no audio settings in its local storage";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                problem ??= $"{folder}: {exception.Message}";
            }
        }
        return new AudioImportResult(null, problem ?? $"{Name} is not installed");
    }

    static string StorageOf(string appData, string folder) => Path.Combine(appData, folder, "Local Storage", "leveldb");

    /// <summary>A local storage value of the first origin that has it: its first byte says Latin-1 (1) or UTF-16 (0).</summary>
    protected static string? Text(IReadOnlyDictionary<string, byte[]> storage, string name, IEnumerable<string> origins)
    {
        foreach (var origin in origins)
        {
            if (!storage.TryGetValue($"_{origin}\0\u0001{name}", out var value) || value.Length == 0) continue;
            return value[0] switch
            {
                1 => Encoding.Latin1.GetString(value, 1, value.Length - 1),
                0 => Encoding.Unicode.GetString(value, 1, value.Length - 1),
                _ => null,
            };
        }
        return null;
    }

    /// <summary>A child object, null when missing or of another kind.</summary>
    protected static JsonElement? Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object ? child : null;

    /// <summary>A string value, null when missing or of another kind.</summary>
    protected static string? Word(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A true or false value, null when missing or of another kind.</summary>
    protected static bool? Flag(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    /// <summary>A number, null when missing or of another kind.</summary>
    protected static double? Number(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
