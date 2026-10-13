using OVS.Client.Audio;
using System.Net;
using OVS.Client.Audio.Devices;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Exceptions;
using OVS.Client.Input;
using System.Text.Json;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.Diagnostics;

/// <summary>
/// Local HTTP API to drive the client without keyboard or mouse (enabled with --debug-api &lt;port&gt;).
/// Listens on localhost only. Every request needs the header "X-OVS-Debug: 1": browsers cannot send custom
/// headers cross-origin without a CORS preflight this server never approves, so web pages cannot use it.
///
/// GET  /state                                   full UI state, audio statistics, notices
/// GET  /devices                                 audio devices (ids for /settings)
/// POST /connect      {host, port, nickname, password?, trust? = true}
/// POST /disconnect
/// POST /join         {channel}                  name or id
/// POST /ptt          {down}                     simulated push-to-talk key
/// POST /linkptt      {down}                     simulated link push-to-talk key
/// POST /server-icon  {path?}                    upload a logo file like the admin page does, without path: remove it
/// POST /key          {action, down?}            any key action (PushToTalk, LinkPushToTalk, PushToMute, ToggleMute, ToggleDeafen);
///                                                without down: press and release once
/// POST /mute         {value}    /deafen {value}
/// POST /tone         {hz}                       test tone instead of microphone, null = microphone
/// POST /redeem       {token}
/// POST /create-channel {name, description?}     /delete-channel {channel}
/// POST /link         {a, b}     /unlink {a, b}  channel names or ids
/// POST /move         {user, channel}            user = nickname or session id
/// POST /kick         {user, reason?}   /ban {user, reason?, minutes?, ip?}   /server-mute {user, value}
/// POST /chat         {target, text, to?}        target: server, channel or private (to = nickname or session id)
/// POST /request      {type, ...}                any protocol request, e.g. {"type":"createGroup",...}
/// POST /settings     ClientSettings JSON        applied like the settings dialog
/// </summary>
public sealed class DebugApi : IDisposable
{
    public const string Header = "X-OVS-Debug";

    readonly HttpListener listener = new();
    readonly MainViewModel vm;
    readonly Func<Func<Task<object?>>, Task<object?>> onUi;
    readonly Func<AudioDirection, List<AudioDevice>> listDevices;
    readonly Task loop;
    readonly KeyPoller keys;
    readonly AudioEngine audio;
    readonly DeviceLifecycle lifecycle;
    readonly Dictionary<string, Func<JsonElement, Task<object?>>> clientRoutes;
    readonly Dictionary<string, Func<ServerViewModel, JsonElement, Task>> serverRoutes;

    /// <param name="keys">The key poller the key routes simulate on and /state reads (from the composition root).</param>
    /// <param name="audio">The audio engine /state reads and /tone reaches the test tone through (from the composition root).</param>
    /// <param name="onUi">Runs a function on the UI thread; every view model access goes through it.</param>
    /// <param name="listDevices">The system's active devices of a direction, which /devices lists (also when the client runs without them).</param>
    public DebugApi(MainViewModel viewModel, KeyPoller keys, AudioEngine audio, int port, Func<Func<Task<object?>>, Task<object?>> onUi, Func<AudioDirection, List<AudioDevice>> listDevices)
    {
        this.keys = keys;
        this.audio = audio;
        lifecycle = audio.DeviceLifecycle; // the /tone route sets the test tone there and reads it back
        clientRoutes = CreateClientRoutes();
        serverRoutes = CreateServerRoutes();
        this.vm = viewModel;
        this.onUi = onUi;
        this.listDevices = listDevices;
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        loop = Task.Run(LoopAsync);
    }

    async Task LoopAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        int status = 200;
        object? result;
        try
        {
            if (context.Request.Headers[Header] is null)
            {
                status = 403;
                result = new { error = $"Header {Header} fehlt" };
            }
            else
            {
                var body = context.Request.HasEntityBody
                    ? (await JsonDocument.ParseAsync(context.Request.InputStream)).RootElement
                    : default;
                var method = context.Request.HttpMethod;
                var path = context.Request.Url!.AbsolutePath.TrimEnd('/');
                result = await onUi(() => DispatchAsync(method, path, body));
            }
        }
        catch (Exception exception) when (exception is DebugApiException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            status = 400;
            result = new { error = exception.Message };
        }
        catch (Exception exception)
        {
            status = 500;
            result = new { error = exception.ToString() };
        }

        try
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.OutputStream, result, ProtocolJson.Options);
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
        {
        }
    }

    async Task<object?> DispatchAsync(string method, string path, JsonElement body)
    {
        if (method == "GET" && path == "/state") return State();
        if (method == "GET" && path == "/devices")
            return new { Inputs = listDevices(AudioDirection.Input), Outputs = listDevices(AudioDirection.Output) };
        if (method != "POST") throw new DebugApiException($"Unbekannter Endpunkt {method} {path}");
        if (clientRoutes.TryGetValue(path, out var clientRoute)) return await clientRoute(body);
        var server = vm.Server ?? throw new DebugApiException("Nicht verbunden");
        if (!serverRoutes.TryGetValue(path, out var serverRoute)) throw new DebugApiException($"Unbekannter Endpunkt {method} {path}");
        await serverRoute(server, body);
        return new { ok = true };
    }

    /// <summary>The POST routes that need no server, path to handler, grouped: connection, keys, audio and settings, server logo.</summary>
    Dictionary<string, Func<JsonElement, Task<object?>>> CreateClientRoutes() => new()
    {
        // connection
        ["/connect"] = async body => { await ConnectAsync(body); return State(); },
        ["/disconnect"] = async _ => { await vm.Connection.DisconnectAsync(); return State(); },
        // keys
        ["/ptt"] = body => { keys.Simulate(ptt: body.GetProperty("down").GetBoolean()); return Done; },
        ["/linkptt"] = body => { keys.Simulate(linkPtt: body.GetProperty("down").GetBoolean()); return Done; },
        ["/key"] = async body => { await SimulateKeyAsync(body); return new { ok = true }; },
        // audio and settings
        ["/tone"] = body =>
        {
            lifecycle.SetTone(Optional(body, "hz")?.GetDouble());
            return Task.FromResult<object?>(new { ok = true, toneHz = lifecycle.CaptureSource.ToneHz });
        },
        ["/settings"] = body => Task.FromResult<object?>(ApplySettings(body)),
        // server logo
        ["/server-icon"] = async body => { await SetServerIconAsync(body); return new { ok = true }; },
    };

    /// <summary>The answer of a route that has nothing to report.</summary>
    static readonly Task<object?> Done = Task.FromResult<object?>(new { ok = true });

    ClientSettings ApplySettings(JsonElement body)
    {
        var settings = body.Deserialize<ClientSettings>(ProtocolJson.Options) ?? throw new DebugApiException("Einstellungen fehlen");
        // Package 127: a body without a version keeps the current one (a missing key reads as 0)
        if (Optional(body, "settingsVersion") is null) settings.SettingsVersion = vm.Settings.SettingsVersion;
        // Package 129: a body without the switch is read like an old file (A135 step 3): a threshold given by hand stays manual
        if (Optional(body, "vadAutomatic") is null) settings.InferVadAutomatic();
        vm.ApplySettings(settings);
        return vm.Settings;
    }

    /// <summary>The POST routes on the connected server, path to handler, grouped: own state, channels, moderation, chat, raw requests.</summary>
    Dictionary<string, Func<ServerViewModel, JsonElement, Task>> CreateServerRoutes() => new()
    {
        // own state
        ["/join"] = (server, body) => server.JoinAsync(Channel(server, Text(body, "channel")).Id),
        ["/mute"] = async (server, body) =>
        {
            if (server.SelfState.SelfMuted != body.GetProperty("value").GetBoolean()) await server.SelfState.ToggleMuteCommand.ExecuteAsync(null);
        },
        ["/deafen"] = async (server, body) =>
        {
            if (server.SelfState.SelfDeafened != body.GetProperty("value").GetBoolean()) await server.SelfState.ToggleDeafenCommand.ExecuteAsync(null);
        },
        // channels
        ["/create-channel"] = (server, body) => server.SendAsync(new CreateChannel(Text(body, "name"), Optional(body, "description")?.GetString() ?? "")),
        ["/delete-channel"] = (server, body) => server.SendAsync(new DeleteChannel(Channel(server, Text(body, "channel")).Id)),
        ["/link"] = (server, body) => server.SendAsync(new LinkChannels(Channel(server, Text(body, "a")).Id, Channel(server, Text(body, "b")).Id)),
        ["/unlink"] = (server, body) => server.SendAsync(new UnlinkChannels(Channel(server, Text(body, "a")).Id, Channel(server, Text(body, "b")).Id)),
        // moderation
        ["/move"] = (server, body) => server.SendAsync(new MoveUser(User(server, Text(body, "user")).SessionId, Channel(server, Text(body, "channel")).Id)),
        ["/kick"] = (server, body) => server.SendAsync(new KickSession(User(server, Text(body, "user")).SessionId, Optional(body, "reason")?.GetString() ?? "")),
        ["/ban"] = (server, body) => server.SendAsync(new BanSession(User(server, Text(body, "user")).SessionId,
            Optional(body, "reason")?.GetString() ?? "",
            Optional(body, "minutes")?.GetInt32(),
            Optional(body, "ip")?.GetBoolean() ?? false)),
        ["/server-mute"] = (server, body) =>
            server.SendAsync(new SetSessionServerMute(User(server, Text(body, "user")).SessionId, body.GetProperty("value").GetBoolean())),
        // chat
        ["/chat"] = (server, body) =>
        {
            var target = Enum.TryParse<ChatTarget>(Text(body, "target"), ignoreCase: true, out var parsed)
                ? parsed : throw new DebugApiException("target ist server, channel oder private");
            uint? to = target == ChatTarget.Private ? User(server, Text(body, "to")).SessionId : null;
            return server.SendChatAsync(target, Text(body, "text"), to);
        },
        // raw requests
        ["/redeem"] = (server, body) => server.SendAsync(new RedeemAdminToken(Text(body, "token"))),
        ["/request"] = (server, body) => server.SendAsync(body.Deserialize<Message>(ProtocolJson.Options) as Request
            ?? throw new DebugApiException("Body ist keine Protokoll-Anfrage (Feld 'type' prüfen)")),
    };


    async Task ConnectAsync(JsonElement body)
    {
        bool trust = Optional(body, "trust")?.GetBoolean() ?? true;
        var previous = vm.ConfirmTofu;
        vm.ConfirmTofu = _ => Task.FromResult(trust);
        try
        {
            await vm.Connection.ConnectAsync(new ConnectChoice(
                Text(body, "host"),
                Optional(body, "port")?.GetInt32() ?? ProtocolDefaults.Port,
                Text(body, "nickname"),
                Optional(body, "password")?.GetString(),
                SaveBookmark: false));
        }
        finally
        {
            vm.ConfirmTofu = previous;
        }
    }

    object State()
    {
        vm.Tick();
        var server = vm.Server;
        return new
        {
            vm.Status,
            Connected = server is not null,
            vm.TransmitText,
            vm.VoiceHint,
            vm.Connection.PingText,
            Connection = new // Package 133: the figures of the signal icon and the connection details
            {
                vm.Stats.Rating,
                vm.Stats.Bars,
                vm.Stats.Unreachable,
                vm.Stats.BaselineMs,
                RoundTrip = vm.Stats.RoundTripText,
                Uplink = vm.Stats.UplinkText,
                Downlink = vm.Stats.DownlinkText,
                Jitter = vm.Stats.JitterText,
                Buffer = vm.Stats.DelayText,
                Concealed = vm.Stats.ConcealedText,
                Bitrate = vm.Stats.BitrateText,
            },
            Notices = vm.Notices.Take(50).Select(notice => notice.ToString()).ToList(),
            Sounds = audio.Sounds.Recent.Select(soundEvent => soundEvent.ToString()).ToList(), // Package 47
            Server = server is null ? null : new
            {
                server.ServerName,
                server.WelcomeText,
                server.IconHash,
                IconBytes = server.IconPng?.Length,
                Chat = server.ChatFeed.Recent.TakeLast(50).Select(chatMessage => new { chatMessage.Target, chatMessage.FromNickname, chatMessage.ToSessionId, chatMessage.Text }).ToList(),
                SelfId = server.Mirror.SelfId,
                server.SelfState.SelfMuted,
                server.SelfState.SelfDeafened,
                server.SelfPermissions,
                server.IsAdmin,
                server.Mirror.DefaultChannelId,
                Channels = server.Tree.Channels.Live().Select(channel => new
                {
                    channel.Id,
                    channel.Name,
                    channel.IsSeparator,
                    channel.Bitrate, // Package 132
                    channel.Description,
                    channel.IsCurrent,
                    channel.IsLinked,
                    channel.LinkedNames,
                    Users = channel.Users.Live().Select(user => new
                    {
                        user.SessionId,
                        user.Nickname,
                        user.IsSelf,
                        user.IsSpeaking,
                        user.IsSpeakingViaLink,
                        user.StatusText,
                        user.ServerMuted,
                        user.GroupNames,
                    }).ToList(),
                }).ToList(),
                server.Mirror.Groups,
                LastUserList = server.LastUserList?.Users,
                LastBanList = server.LastBanList?.Bans,
            },
            Audio = new
            {
                audio.FramesSent,
                audio.FramesReceived,
                audio.LastOutputLevelDb,
                lifecycle.CaptureSource.ToneHz,
                Ptt = keys.PttDown,
                LinkPtt = keys.LinkPttDown,
                PushToMute = keys.MuteHeld,
            },
            KeyBindings = vm.Settings.KeyBindings.Select(keyBinding => new { keyBinding.Action, Chord = keyBinding.Chord.Name }).ToList(),
        };
    }

    /// <summary>{path}: checks and scales the image like the admin page; without a path the logo is removed.</summary>
    async Task SetServerIconAsync(JsonElement body)
    {
        var server = vm.Server ?? throw new DebugApiException("Nicht verbunden");
        if (Optional(body, "path")?.GetString() is not { } path)
        {
            await server.SendAsync(new SetServerIcon(null));
            return;
        }
        var (png, error) = Services.IconImport.Prepare(path);
        if (png is null) throw new DebugApiException(error ?? "Bild ungültig");
        await server.SendAsync(new SetServerIcon(Convert.ToBase64String(png)));
    }

    /// <summary>Hold actions take "down"; without it the key is pressed and released, which fires a toggle once.</summary>
    async Task SimulateKeyAsync(JsonElement body)
    {
        var name = Text(body, "action");
        if (!Enum.TryParse<KeyAction>(name, ignoreCase: true, out var action))
            throw new DebugApiException($"Unbekannte Aktion '{name}', erlaubt: {string.Join(", ", KeyActionTraits.All)}");
        if (Optional(body, "down") is { } down)
        {
            keys.Simulate(action, down.GetBoolean());
            return;
        }
        keys.Simulate(action, true);
        await Task.Delay(50); // clock: debug, a few polls of the key thread
        keys.Simulate(action, false);
        await Task.Delay(30); // clock: debug
    }

    static JsonElement? Optional(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    static string Text(JsonElement body, string name) =>
        Optional(body, name) is { } value
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
            : throw new DebugApiException($"Feld '{name}' fehlt");

    /// <summary>By id or name; Package 110: a name several channels carry needs the id.</summary>
    static ChannelViewModel Channel(ServerViewModel server, string key)
    {
        var live = server.Tree.Channels.Live().ToList();
        if (live.FirstOrDefault(channel => channel.Id.ToString() == key) is { } byId) return byId;
        var named = live.Where(channel => string.Equals(channel.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        return named.Count switch
        {
            1 => named[0],
            0 => throw new DebugApiException($"Channel '{key}' nicht gefunden"),
            _ => throw new DebugApiException($"Channel '{key}' gibt es mehrfach, bitte per Id: {string.Join(", ", named.Select(channel => channel.Id))}"),
        };
    }

    static UserViewModel User(ServerViewModel server, string key) =>
        server.Tree.Channels.Live().SelectMany(channel => channel.Users.Live())
            .FirstOrDefault(user => user.SessionId.ToString() == key || string.Equals(user.Nickname, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new DebugApiException($"Nutzer '{key}' nicht gefunden");

    public void Dispose()
    {
        listener.Close();
        try
        {
            loop.Wait(TimeSpan.FromSeconds(1)); // clock: debug
        }
        catch (AggregateException)
        {
        }
    }
}
