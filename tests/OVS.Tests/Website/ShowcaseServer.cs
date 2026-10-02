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

    public const uint Self = 1, Mara = 2, Jonas = 3, Lea = 4, Tim = 5, Nora = 6, Ben = 7, Kai = 8, Finn = 9, Ole = 10;

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
            User(Tim, "Tim", Training, WellKnownGroups.Guest),
            User(Nora, "Nora", Training, Member),
            User(Ben, "Ben", Training, WellKnownGroups.Guest),
            User(Kai, "Kai", Lobby, WellKnownGroups.Guest),
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
