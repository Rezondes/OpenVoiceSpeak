using OVS.Client.Audio;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using SkiaSharp;

namespace OVS.Tests.Website;

/// <summary>
/// Package 116 (A126): the made-up server the website's images show, "Gilde Nordlicht", in German or English. No network,
/// fixed data and times, so the images only change when the client does.
/// </summary>
public sealed class ShowcaseServer
{
    public static readonly Guid Lobby = new("0b0b0b0b-0000-0000-0000-000000000001"), Raid = new("0b0b0b0b-0000-0000-0000-000000000002"),
        Strategy = new("0b0b0b0b-0000-0000-0000-000000000003"), Training = new("0b0b0b0b-0000-0000-0000-000000000004"),
        Afk = new("0b0b0b0b-0000-0000-0000-000000000005"), SeparatorA = new("0b0b0b0b-0000-0000-0000-000000000006"),
        SeparatorB = new("0b0b0b0b-0000-0000-0000-000000000007"), Officers = new("0b0b0b0b-0000-0000-0000-000000000008");

    public static readonly Guid Member = new("0c0c0c0c-0000-0000-0000-000000000001"), Moderator = new("0c0c0c0c-0000-0000-0000-000000000002");

    /// <summary>The evening all the chat lines and statistics are from, 20:15 on the clock of the machine that renders.</summary>
    public static readonly DateTimeOffset Evening = new(new DateTime(2026, 10, 1, 20, 15, 0, DateTimeKind.Local));

    public const uint Self = 1, Mara = 2, Jonas = 3, Lea = 4, Nora = 6, Kai = 8, Finn = 9, Ole = 10;

    public bool English { get; }
    public ServerViewModel Server { get; }
    public ManualTimeProvider Time { get; } = new(Evening);
    public List<Request> Sent { get; } = [];

    /// <summary>Answers the server would send to a request (admin lists in Package 117); null = none.</summary>
    public Func<Request, Message?>? Reply { get; set; }

    public string T(string de, string en) => English ? en : de;

    public ShowcaseServer(bool english, Dialogs? dialogs = null)
    {
        English = english;
        var everyone = Permission.Speak | Permission.ChatServer | Permission.ChatChannel | Permission.ChatPrivate;
        var groups = new[]
        {
            new GroupInfo(WellKnownGroups.Guest, T("Gast", "Guest"), Permission.Speak | Permission.ChatChannel, true),
            new GroupInfo(Member, T("Mitglied", "Member"), everyone | Permission.SpeakLinked, true),
            new GroupInfo(Moderator, "Moderator", everyone | Permission.SpeakLinked | Permission.UserMove | Permission.UserMute
                | Permission.UserKick | Permission.ChannelLink, true),
            new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All, true),
        };
        var channels = new[]
        {
            new ChannelInfo(Lobby, "Lobby", T("Willkommen! Schau dich um.", "Welcome! Have a look around."), 0),
            new ChannelInfo(SeparatorA, "", "", 1, Kind: ChannelKind.Separator),
            new ChannelInfo(Raid, T("Raid Donnerstag", "Thursday raid"), T("Donnerstags ab 20 Uhr", "Thursdays from 8 pm"), 2),
            new ChannelInfo(Strategy, T("Strategie", "Strategy"), "", 3),
            new ChannelInfo(Training, T("Ausbildung", "Training"), T("Für Neue, höchstens fünf", "For new members, five at most"), 4, MaxUsers: 5),
            new ChannelInfo(Officers, T("Offiziere", "Officers"), "", 5, AllowedGroupIds: [Moderator], HasPassword: true),
            new ChannelInfo(SeparatorB, "", "", 6, Kind: ChannelKind.Separator),
            new ChannelInfo(Afk, "AFK", T("Hier ist es still", "Quiet in here"), 7, IsMuted: true),
        };
        UserInfo User(uint id, string nick, Guid channel, Guid group, bool muted = false, bool deafened = false) =>
            new(id, $"fp{id:D2}", nick, channel, muted || deafened, deafened, false, Permission.None, [group], CanBeModeratedByMe: true);
        var users = new[]
        {
            new UserInfo(Self, "fp01", "Steffi", Raid, false, false, false, Permission.All, [WellKnownGroups.Admin]),
            User(Mara, "Mara", Raid, Moderator),
            User(Jonas, "Jonas", Raid, Member),
            User(Lea, "Lea", Raid, Member, muted: true),
            User(Nora, "Nora", Training, Member),
            User(Kai, "Kai", Training, WellKnownGroups.Guest),
            User(Finn, "Finn", Strategy, Moderator),
            User(Ole, "Ole", Afk, Member, deafened: true),
        };
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde Nordlicht", T("Schön, dass du da bist!", "Good to have you here!"), true,
                null, new ServerLimits(50, 30, true, true, new TimeOnly(4, 0))), Lobby, channels,
            [new LinkInfo(Raid, Strategy)], groups, users);
        Server = new ServerViewModel(new StateMirror(new Welcome(Self, "", snapshot)), request =>
        {
            Sent.Add(request);
            if (Reply?.Invoke(request) is { } answer) Server!.Apply(answer);
            return Task.CompletedTask;
        }, Time, dialogs) { IconPng = Logo() };
    }

    /// <summary>A chat line from someone at a minute of the evening.</summary>
    public ChatMessage Say(uint from, string text, int minute, Guid? channel = null, uint? to = null)
    {
        var nick = Server.Mirror.Users[from].Nickname;
        var target = to is not null ? ChatTarget.Private : channel is not null ? ChatTarget.Channel : ChatTarget.Server;
        return new ChatMessage(target, from, nick, channel, to, text, Evening.AddMinutes(minute));
    }

    /// <summary>The channel chat of the raid, as on the hero image.</summary>
    public void FillRaidChat()
    {
        Server.Apply(Say(Mara, T("Alle da? Wir starten in fünf Minuten.", "Everyone here? We start in five minutes."), 0, Raid));
        Server.Apply(Say(Jonas, T("Bin bereit, Tränke sind dabei.", "Ready, potions packed."), 1, Raid));
        Server.Apply(Say(Self, T("Strategie hört über den Link mit.", "Strategy listens in through the link."), 2, Raid));
        Server.Apply(Say(Lea, T("Komme gleich, Mikro ist noch aus.", "Coming, my mic is still off."), 3, Raid));
    }

    // ---- Package 117: what the administration shows ----

    /// <summary>Answers the administration's requests (users, bans, backups, logs) and joins, as the server would.</summary>
    public void AnswerAdministration() => Reply = request => request switch
    {
        ListUsers r => new UserList(r.RequestId, KnownUsers(), 0, KnownUsers().Count),
        ListBans r => new BanList(r.RequestId, Bans(), 0, Bans().Count),
        ListBackups r => new BackupList(r.RequestId, Backups(), 0, Backups().Count),
        ListLogs r => new LogList(r.RequestId, LogFiles(), 0, LogFiles().Count),
        ReadLog r => new LogPage(r.RequestId, r.FileId, 1, 1, 1, LogLines(r.FileId)),
        SearchLogs r => new LogSearchResult(r.RequestId, LogLines(RaidLog).Select((text, i) => new LogHit(RaidLog, i + 1, text))
            .Where(h => h.Text.Contains(r.Query, StringComparison.OrdinalIgnoreCase)).ToList(), false, false),
        JoinChannel r => new UserUpdated(Server.Mirror.Users[Self] with { ChannelId = r.ChannelId }),
        _ => null,
    };

    DateTimeOffset Ago(int days, int hours = 0) => Evening.AddDays(-days).AddHours(-hours);

    IReadOnlyList<KnownUserInfo> KnownUsers() =>
    [
        new("fp02", "Mara", [Moderator], Ago(210), Evening, 312, TimeSpan.FromHours(486), PreviousNicknames: ["Marabelle"], SpeechTime: TimeSpan.FromHours(61),
            ChatMessages: 1840, IsOnline: true, SessionId: Mara),
        new("fp03", "Jonas", [Member], Ago(120), Evening, 154, TimeSpan.FromHours(201), SpeechTime: TimeSpan.FromHours(23), ChatMessages: 512,
            IsOnline: true, SessionId: Jonas),
        new("fp08", "Kai", [WellKnownGroups.Guest], Ago(2), Evening, 3, TimeSpan.FromHours(4), ChatMessages: 12, IsOnline: true, SessionId: Kai),
        new("fp11", "Sven", [Member], Ago(300), Ago(9), 98, TimeSpan.FromHours(140), SpeechTime: TimeSpan.FromHours(12), ChatMessages: 230),
        new("fp12", "Rieke", [Member, Moderator], Ago(400), Ago(1, 3), 401, TimeSpan.FromHours(690), SpeechTime: TimeSpan.FromHours(88), ChatMessages: 2911),
        new("fp13", "Paul", [WellKnownGroups.Guest], Ago(30), Ago(14), 4, TimeSpan.FromHours(3), Bans: [Bans()[0]]),
    ];

    IReadOnlyList<BanInfo> Bans() =>
    [
        new(new Guid("0d0d0d0d-0000-0000-0000-000000000001"), "fp13", "Paul", null, T("Spam im Chat", "Spamming the chat"), "Mara", Evening.AddDays(3),
            Ago(4), "fp02", 7 * 24 * 60, BlockedAttempts: 2, LastAttempt: Ago(1)),
        new(new Guid("0d0d0d0d-0000-0000-0000-000000000002"), "fp14", "Griefer42", "203.0.113.7", T("Beleidigungen", "Insults"), "Steffi", null,
            Ago(40), "fp01", BlockedAttempts: 9, LastAttempt: Ago(6), LastAttemptIp: "203.0.113.7"),
        new(new Guid("0d0d0d0d-0000-0000-0000-000000000003"), "fp15", "Lukas", null, T("Abgesprochen: Pause", "Agreed break"), "Mara", Ago(10),
            Ago(17), "fp02", 7 * 24 * 60, LiftedAt: Ago(12), LiftedBy: "Steffi"),
    ];

    IReadOnlyList<BackupInfo> Backups() =>
    [
        new("2026-10-01_04-00.zip", Ago(0, 16), 48_212, "011026.0h4v"),
        new("2026-09-30_04-00.zip", Ago(1, 16), 47_980, "300926.0f1a"),
        new("2026-09-29_04-00.zip", Ago(2, 16), 47_655, "290926.0c7e"),
    ];

    public const string ServerLog = "server/2026-10-01.log", RaidLog = "channel/raid/2026-10-01.log";

    IReadOnlyList<LogFileInfo> LogFiles() =>
    [
        new(ServerLog, LogKind.Server, null, null, Ago(0, 20), Evening, 18_430),
        new(RaidLog, LogKind.Channel, Raid, Server.Mirror.Channels[Raid].Name, Ago(0, 2), Evening, 2_210),
        new("channel/training/2026-10-01.log", LogKind.Channel, Training, Server.Mirror.Channels[Training].Name, Ago(0, 3), Evening.AddMinutes(-20), 1_120),
        new("server/2026-09-30.log", LogKind.Server, null, null, Ago(1, 20), Ago(0, 20), 22_904),
    ];

    IReadOnlyList<string> LogLines(string file)
    {
        string At(int minute) => Evening.AddMinutes(minute - 30).ToString("yyyy-MM-dd HH:mm:ss");
        return file == RaidLog
            ?
            [
                $"{At(0)} Steffi {T("hat den Channel betreten (kommt aus Lobby)", "entered the channel (from Lobby)")}",
                $"{At(4)} Mara {T("hat den Channel betreten (kommt aus Lobby)", "entered the channel (from Lobby)")}",
                $"{At(9)} Jonas {T("hat den Channel betreten (kommt aus Strategie)", "entered the channel (from Strategy)")}",
                $"{At(12)} {T("Link zu Strategie gesetzt von Mara", "Link to Strategy set by Mara")}",
                $"{At(18)} Lea {T("hat den Channel betreten (kommt aus AFK)", "entered the channel (from AFK)")}",
                $"{At(22)} {T("Channel geändert von Steffi: Beschreibung geändert", "Channel changed by Steffi: description changed")}",
                $"{At(27)} Jonas {T("wurde von Mara stummgeschaltet", "was muted by Mara")}",
                $"{At(28)} Jonas {T("Stummschaltung durch Mara aufgehoben", "unmuted by Mara")}",
            ]
            :
            [
                $"{At(-60)} OpenVoiceSpeak-Server {T("startet", "starting")}",
                $"{At(-59)} {T("Lauscht auf 0.0.0.0:7000 (TCP und UDP)", "Listening on 0.0.0.0:7000 (TCP and UDP)")}",
                $"{At(0)} Steffi {T("verbunden", "connected")}",
                $"{At(4)} Mara {T("verbunden", "connected")}",
                $"{At(9)} Jonas {T("verbunden", "connected")}",
                $"{At(15)} {T("Backup erstellt", "Backup created")}",
                $"{At(20)} Paul {T("abgewiesen: gebannt", "refused: banned")}",
            ];
    }

    /// <summary>Mara speaks in the raid, Finn over the link from strategy.</summary>
    public void Speak() => Server.OnSpeakers([new ActiveSpeaker(Mara, false), new ActiveSpeaker(Finn, true)]);

    /// <summary>The guild's logo: a northern light over a dark blue round.</summary>
    static byte[] Logo()
    {
        const int size = 128;
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x10, 0x1A, 0x3A));
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, size), new SKPoint(size, 0),
                [new SKColor(0x2F, 0xD2, 0x9B), new SKColor(0x2F, 0x6F, 0xEB), new SKColor(0x9B, 0x5C, 0xF6)], SKShaderTileMode.Clamp),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 14,
            StrokeCap = SKStrokeCap.Round,
        };
        using var path = new SKPath();
        path.MoveTo(18, 92);
        path.CubicTo(40, 40, 70, 96, 110, 34);
        canvas.DrawPath(path, glow);
        using var star = new SKPaint { IsAntialias = true, Color = SKColors.White };
        canvas.DrawCircle(96, 92, 5, star);
        canvas.DrawCircle(32, 34, 3, star);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
