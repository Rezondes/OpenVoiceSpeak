using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Settings;

/// <summary>The client's persisted files of earlier versions load with every stored value as it was written.</summary>
public sealed class ClientFileFixtureTests() : TempDirectoryTest("ovs-fixture-", quiet: false)
{
    const string KnownServersFile = "known_servers.json";
    const string FirstPin = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    const string SecondPin = "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210";

    [Theory]
    [MemberData(nameof(PersistedFixtures.Versions), MemberType = typeof(PersistedFixtures))]
    public void Settings_FixtureOfVersion_LoadsEveryStoredValue(string version)
    {
        PersistedFixtures.CopyTo(version, ClientSettings.FileName, dir);

        var settings = ClientSettings.Load(dir, out var warning);

        Assert.Null(warning);
        Assert.Equivalent(Expected(version), settings, strict: true);
        Assert.Equal(Order(Expected(version)), Order(settings)); // Equivalent ignores the order of list entries
    }

    /// <summary>
    /// Saving the current version's file again writes it byte for byte: no key added, dropped, renamed or moved. Line
    /// breaks are compared normalized: the writer uses the platform's line break, and a Windows checkout turns the
    /// stored LF into CRLF. The generator wrote the key names with the invariant UI culture, so the save does too.
    /// </summary>
    [Fact]
    public void Settings_CurrentFixtureSavedAgain_IsByteIdentical()
    {
        var original = File.ReadAllBytes(PersistedFixtures.CopyTo(PersistedFixtures.Current, ClientSettings.FileName, dir));
        var settings = ClientSettings.Load(dir, out var warning);
        Assert.Null(warning);

        TestCulture.With("", () =>
        {
            settings.Save(dir);
            return 0;
        });

        Assert.Equal(WithLf(original), WithLf(File.ReadAllBytes(Path.Combine(dir, ClientSettings.FileName))));
    }

    static byte[] WithLf(byte[] text) => [.. text.Where((b, index) => !(b == '\r' && index + 1 < text.Length && text[index + 1] == '\n'))];

    [Theory]
    [MemberData(nameof(PersistedFixtures.Versions), MemberType = typeof(PersistedFixtures))]
    public void KnownServers_FixtureOfVersion_KeepsEveryPin(string version)
    {
        PersistedFixtures.CopyTo(version, KnownServersFile, dir); // read back by the name the code uses: the name on disk is pinned too
        var known = new TrustedServerFingerprints(dir);

        Assert.Equal(TofuResult.Known, known.Check(" Voice.Example.org", 9987, FirstPin));
        Assert.Equal(TofuResult.Known, known.Check("127.0.0.1", 9000, SecondPin));
        Assert.Equal(TofuResult.Mismatch, known.Check("127.0.0.1", 9000, FirstPin));
        Assert.Equal(TofuResult.Unknown, known.Check("127.0.0.1", 9001, SecondPin));
    }

    static string[] Order(ClientSettings clientSettings) => [.. clientSettings.Bookmarks.Select(bookmark => bookmark.Name), .. clientSettings.KeyBindings.Select(keyBinding => $"{keyBinding.Action}")];

    /// <summary>
    /// What the fixture generator stored. The release wrote no settings version and none of the later keys, so its file
    /// gets their defaults and runs every migration step: automatic gain on, and the voice gate stays manual for its tuned threshold.
    /// </summary>
    static ClientSettings Expected(string version)
    {
        var settings = new ClientSettings
        {
            Bookmarks =
            [
                new Bookmark("Zuhause", "voice.example.org", 9987, "Anna"),
                new Bookmark("Mit Passwort", "192.0.2.20", 9000, "Anna", ProtectedPassword: "bm90LWEtZHBhcGktYmxvYg=="),
            ],
            InputDeviceId = "{0.0.1.00000000}.{mic}",
            OutputDeviceId = "{0.0.0.00000000}.{speaker}",
            InputGain = 1.5f,
            OutputVolume = 0.9f,
            Mode = TransmitMode.VoiceActivation,
            KeyBindings =
            [
                new KeyBinding(KeyAction.PushToTalk, new KeyChord(0x56, ChordModifiers.Ctrl | ChordModifiers.Shift)),
                new KeyBinding(KeyAction.ToggleMute, new KeyChord(0x05)),
            ],
            VadThresholdDb = -35f,
            VadAutomatic = false,
            Theme = AppTheme.Dark,
            Language = AppLanguage.English,
            CheckForUpdates = false,
            SoundVolume = 0.5f,
            Sounds = new() { [SoundEvent.UserJoined] = new SoundSetting(Volume: 0.5f), [SoundEvent.PrivateMessage] = new SoundSetting(Muted: true, File: "ping.wav") },
            UserVolumes = new() { [new string('b', 64)] = 0.7f },
            BackgroundOpacity = 0.6f,
            BlurBackground = true,
            Display = DisplayMode.Simplified,
        };
        if (version == PersistedFixtures.Current)
        {
            settings.PttReleaseDelayMs = 150;
            settings.NoiseSuppression = false;
            settings.AutoGain = false;
            settings.EchoCancellation = false;
            settings.AudioContent = AudioContent.Music;
        }
        return settings;
    }
}
