using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Settings;

/// <summary>
/// Step 30 (18b review, OCP): the settings page keeps three hand-made lists, the copy of the settings in its constructor,
/// the preview hooks and <see cref="SettingsViewModel.ToSettings"/>. A setting missing in one of them is lost on save or
/// not heard at once.
/// </summary>
public sealed class SettingsRoundTripTests
{
    /// <summary>Old files' keys that the loader moves into the key bindings; the page never shows them.</summary>
    static readonly string[] NotOnThePage = [nameof(ClientSettings.PttKey), nameof(ClientSettings.LinkPttKey)];

    /// <summary>Every setting the page shows away from its default, with values the percent sliders carry exactly.</summary>
    static ClientSettings AllChanged() => new()
    {
        Bookmarks = [new Bookmark("Raid", "raid.example", 7001, "anna")],
        InputDeviceId = FakeAudioHardware.Headset.Id,
        OutputDeviceId = FakeAudioHardware.Speakers.Id,
        InputGain = 0.5f,
        OutputVolume = 1.5f,
        Mode = TransmitMode.VoiceActivation,
        KeyBindings = [new KeyBinding(KeyAction.ToggleMute, new KeyChord(0x70))],
        PttReleaseDelayMs = 120,
        VadThresholdDb = -45,
        VadAutomatic = false,
        NoiseSuppression = false,
        AutoGain = false,
        EchoCancellation = false,
        AudioContent = AudioContent.Music,
        Theme = AppTheme.Dark,
        Language = AppLanguage.English,
        CheckForUpdates = false,
        SoundsEnabled = false,
        SoundVolume = 0.25f,
        Sounds = new() { [SoundEvent.MicOn] = new SoundSetting(Muted: true), [SoundEvent.UserJoined] = new SoundSetting(Volume: 0.5f) }, // in the page's order
        UserVolumes = new() { ["fingerprint"] = 0.5f },
        BackgroundOpacity = 0.5f,
        BlurBackground = true,
        Display = DisplayMode.Animated,
        StreamerMode = true,
        Compact = true,
        ChatTextScale = 120,
        NotifyPrivate = false,
        NotifyMention = false,
        NotifyJoin = true,
        ShowMemberList = false,
    };

    static SettingsViewModel Page(ClientSettings settings) =>
        new(settings, [FakeAudioHardware.Headset], [FakeAudioHardware.Speakers], TimeProvider.System);

    [Fact]
    public void AllChanged_EverySetting_DiffersFromItsDefault()
    {
        var changed = AllChanged();
        var defaults = new ClientSettings();
        var same = typeof(ClientSettings).GetProperties()
            .Where(property => property.CanWrite && !NotOnThePage.Contains(property.Name) && property.Name != nameof(ClientSettings.SettingsVersion))
            .Where(property => System.Text.Json.JsonSerializer.Serialize(property.GetValue(changed)) == System.Text.Json.JsonSerializer.Serialize(property.GetValue(defaults)))
            .Select(property => property.Name);

        Assert.Empty(same); // a new setting is added here first, so the round trip below covers it
    }

    [Fact]
    public void ToSettings_PageUntouched_GivesBackEverySetting()
    {
        var settings = AllChanged();

        var saved = Page(settings).ToSettings(settings);

        Assert.Equal(settings.ToJson(), saved.ToJson());
    }

    static readonly Dictionary<string, PreviewedChange> Changes = new()
    {
        ["voice activation"] = new(page => page.VoiceActivation = true, previewed => previewed.Mode == TransmitMode.VoiceActivation),
        ["background opacity"] = new(page => page.BackgroundOpacityPercent = 50, previewed => previewed.BackgroundOpacity == 0.5f),
        ["blur"] = new(page => page.BlurBackground = true, previewed => previewed.BlurBackground),
        ["streamer mode"] = new(page => page.StreamerMode = true, previewed => previewed.StreamerMode),
        ["compact"] = new(page => page.Compact = true, previewed => previewed.Compact),
        ["chat text size"] = new(page => page.ChatTextScale = 125, previewed => previewed.ChatTextScale == 125),
        ["noise suppression"] = new(page => page.NoiseSuppression = false, previewed => !previewed.NoiseSuppression),
        ["auto gain"] = new(page => page.AutoGain = false, previewed => !previewed.AutoGain),
        ["echo cancellation"] = new(page => page.EchoCancellation = false, previewed => !previewed.EchoCancellation),
        ["music"] = new(page => page.IsMusic = true, previewed => previewed.AudioContent == AudioContent.Music),
        ["automatic threshold"] = new(page => page.VadAutomatic = false, previewed => !previewed.VadAutomatic),
        ["display"] = new(page => page.SelectedDisplay = SettingsViewModel.Displays.Single(option => option.Value == DisplayMode.Animated), previewed => previewed.Display == DisplayMode.Animated),
        ["input gain"] = new(page => page.InputGainPercent = 50, previewed => previewed.InputGain == 0.5f),
        ["output volume"] = new(page => page.OutputVolumePercent = 150, previewed => previewed.OutputVolume == 1.5f),
        ["release delay"] = new(page => page.PttReleaseDelayMs = 120, previewed => previewed.PttReleaseDelayMs == 120),
        ["threshold"] = new(page => page.VadThresholdDb = -45, previewed => previewed.VadThresholdDb == -45f),
    };

    public static TheoryData<string> PreviewedChanges => new(Changes.Keys);

    [Theory]
    [MemberData(nameof(PreviewedChanges))]
    public void Change_OnThePage_PreviewsAtOnce(string setting)
    {
        var (change, shows) = Changes[setting];
        var page = Page(new ClientSettings());
        var previews = new List<ClientSettings>();
        page.LivePreview = previews.Add;

        change(page);

        Assert.True(previews.Count > 0 && shows(previews[^1]), $"{setting} is not previewed at once");
    }

    /// <summary>A change made on the page, and how the previewed settings show it.</summary>
    sealed record PreviewedChange(Action<SettingsViewModel> Change, Func<ClientSettings, bool> Shows);
}
