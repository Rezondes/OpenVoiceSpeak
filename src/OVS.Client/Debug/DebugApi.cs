using System.Net;
using NAudio.CoreAudioApi;
using OVS.Client.Audio;
using OVS.Client.Input;
using System.Text.Json;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;

namespace OVS.Client.Debug;

public sealed class DebugApiException(string message) : Exception(message);

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
    readonly Task loop;

    /// <param name="onUi">Runs a function on the UI thread; every view model access goes through it.</param>
    public DebugApi(MainViewModel vm, int port, Func<Func<Task<object?>>, Task<object?>> onUi)
    {
        this.vm = vm;
        this.onUi = onUi;
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
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
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
        catch (Exception e) when (e is DebugApiException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            status = 400;
            result = new { error = e.Message };
        }
        catch (Exception e)
        {
            status = 500;
            result = new { error = e.ToString() };
        }

        try
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.OutputStream, result, ProtocolJson.Options);
            context.Response.Close();
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
        {
        }
    }

    async Task<object?> DispatchAsync(string method, string path, JsonElement body)
    {
        if (method == "GET" && path == "/state") return State();
        if (method == "GET" && path == "/devices")
            return new { Inputs = AudioDevices.List(DataFlow.Capture), Outputs = AudioDevices.List(DataFlow.Render) };
        if (method != "POST") throw new DebugApiException($"Unbekannter Endpunkt {method} {path}");

        switch (path)
        {
            case "/connect":
                await ConnectAsync(body);
                return State();
            case "/disconnect":
                await vm.DisconnectAsync();
                return State();
            case "/ptt":
                vm.Keys.Simulate(ptt: body.GetProperty("down").GetBoolean());
                return new { ok = true };
            case "/linkptt":
                vm.Keys.Simulate(linkPtt: body.GetProperty("down").GetBoolean());
                return new { ok = true };
            case "/key":
                await SimulateKeyAsync(body);
                return new { ok = true };
            case "/server-icon":
                await SetServerIconAsync(body);
                return new { ok = true };
            case "/tone":
                vm.Audio.SetTone(Optional(body, "hz")?.GetDouble());
                return new { ok = true, toneHz = vm.Audio.ToneHz };
            case "/settings":
                vm.ApplySettings(body.Deserialize<ClientSettings>(ProtocolJson.Options) ?? throw new DebugApiException("Einstellungen fehlen"));
                return vm.Settings;
        }

        var server = vm.Server ?? throw new DebugApiException("Nicht verbunden");
        switch (path)
        {
            case "/join":
                await server.JoinAsync(Channel(server, Text(body, "channel")).Id);
                break;
            case "/mute":
                if (server.SelfMuted != body.GetProperty("value").GetBoolean()) await server.ToggleMuteCommand.ExecuteAsync(null);
                break;
            case "/deafen":
                if (server.SelfDeafened != body.GetProperty("value").GetBoolean()) await server.ToggleDeafenCommand.ExecuteAsync(null);
                break;
            case "/redeem":
                await server.SendAsync(new RedeemAdminToken(Text(body, "token")));
                break;
            case "/create-channel":
                await server.CreateChannelAsync(Text(body, "name"), Optional(body, "description")?.GetString() ?? "");
                break;
            case "/delete-channel":
                await server.DeleteChannelAsync(Channel(server, Text(body, "channel")).Id);
                break;
            case "/link":
                await server.LinkAsync(Channel(server, Text(body, "a")).Id, Channel(server, Text(body, "b")).Id);
                break;
            case "/unlink":
                await server.UnlinkAsync(Channel(server, Text(body, "a")).Id, Channel(server, Text(body, "b")).Id);
                break;
            case "/move":
                await server.MoveAsync(User(server, Text(body, "user")).SessionId, Channel(server, Text(body, "channel")).Id);
                break;
            case "/kick":
                await server.KickAsync(User(server, Text(body, "user")).SessionId, Optional(body, "reason")?.GetString() ?? "");
                break;
            case "/ban":
                await server.BanAsync(User(server, Text(body, "user")).SessionId, new BanChoice(
                    Optional(body, "reason")?.GetString() ?? "",
                    Optional(body, "minutes")?.GetInt32(),
                    Optional(body, "ip")?.GetBoolean() ?? false));
                break;
            case "/server-mute":
                await server.ServerMuteAsync(User(server, Text(body, "user")).SessionId, body.GetProperty("value").GetBoolean());
                break;
            case "/chat":
                var target = Enum.TryParse<ChatTarget>(Text(body, "target"), ignoreCase: true, out var t)
                    ? t : throw new DebugApiException("target ist server, channel oder private");
                uint? to = target == ChatTarget.Private ? User(server, Text(body, "to")).SessionId : null;
                await server.SendChatAsync(target, Text(body, "text"), to);
                break;
            case "/request":
                var request = body.Deserialize<Message>(ProtocolJson.Options) as Request
                              ?? throw new DebugApiException("Body ist keine Protokoll-Anfrage (Feld 'type' prüfen)");
                await server.SendAsync(request);
                break;
            default:
                throw new DebugApiException($"Unbekannter Endpunkt {method} {path}");
        }
        return new { ok = true };
    }

    async Task ConnectAsync(JsonElement body)
    {
        bool trust = Optional(body, "trust")?.GetBoolean() ?? true;
        var previous = vm.ConfirmTofu;
        vm.ConfirmTofu = _ => Task.FromResult(trust);
        try
        {
            await vm.ConnectAsync(new ConnectChoice(
                Text(body, "host"),
                Optional(body, "port")?.GetInt32() ?? ProtocolInfo.DefaultPort,
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
        var s = vm.Server;
        return new
        {
            vm.Status,
            Connected = s is not null,
            vm.TransmitText,
            vm.VoiceHint,
            vm.PingText,
            Notices = vm.Notices.Take(50).Select(n => n.ToString()).ToList(),
            Server = s is null ? null : new
            {
                s.ServerName,
                s.WelcomeText,
                s.IconHash,
                IconBytes = s.IconPng?.Length,
                Chat = s.RecentChat.TakeLast(50).Select(c => new { c.Target, c.FromNickname, c.ToSessionId, c.Text }).ToList(),
                SelfId = s.Mirror.SelfId,
                s.SelfMuted,
                s.SelfDeafened,
                s.SelfPermissions,
                s.IsAdmin,
                s.Mirror.DefaultChannelId,
                Channels = s.Channels.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Description,
                    c.IsCurrent,
                    c.IsLinked,
                    c.LinkedNames,
                    Users = c.Users.Select(u => new
                    {
                        u.SessionId,
                        u.Nickname,
                        u.IsSelf,
                        u.IsSpeaking,
                        u.IsSpeakingViaLink,
                        u.StatusText,
                        u.ServerMuted,
                        u.GroupNames,
                    }).ToList(),
                }).ToList(),
                s.Mirror.Groups,
                LastUserList = s.LastUserList?.Users,
                LastBanList = s.LastBanList?.Bans,
            },
            Audio = new
            {
                vm.Audio.FramesSent,
                vm.Audio.FramesReceived,
                vm.Audio.LastOutputLevelDb,
                vm.Audio.ToneHz,
                Ptt = vm.Keys.PttDown,
                LinkPtt = vm.Keys.LinkPttDown,
                PushToMute = vm.Keys.MuteHeld,
            },
            KeyBindings = vm.Settings.KeyBindings.Select(b => new { b.Action, Chord = b.Chord.Name }).ToList(),
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
        var (png, error) = Views.IconImport.Prepare(path);
        if (png is null) throw new DebugApiException(error ?? "Bild ungültig");
        await server.SendAsync(new SetServerIcon(Convert.ToBase64String(png)));
    }

    /// <summary>Hold actions take "down"; without it the key is pressed and released, which fires a toggle once.</summary>
    async Task SimulateKeyAsync(JsonElement body)
    {
        var name = Text(body, "action");
        if (!Enum.TryParse<KeyAction>(name, ignoreCase: true, out var action))
            throw new DebugApiException($"Unbekannte Aktion '{name}', erlaubt: {string.Join(", ", KeyActions.All)}");
        if (Optional(body, "down") is { } down)
        {
            vm.Keys.Simulate(action, down.GetBoolean());
            return;
        }
        vm.Keys.Simulate(action, true);
        await Task.Delay(50); // a few polls of the key thread
        vm.Keys.Simulate(action, false);
        await Task.Delay(30);
    }

    static JsonElement? Optional(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    static string Text(JsonElement body, string name) =>
        Optional(body, name) is { } value
            ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
            : throw new DebugApiException($"Feld '{name}' fehlt");

    static ChannelViewModel Channel(ServerViewModel server, string key) =>
        server.Channels.FirstOrDefault(c => c.Id.ToString() == key || string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new DebugApiException($"Channel '{key}' nicht gefunden");

    static UserViewModel User(ServerViewModel server, string key) =>
        server.Channels.SelectMany(c => c.Users)
            .FirstOrDefault(u => u.SessionId.ToString() == key || string.Equals(u.Nickname, key, StringComparison.OrdinalIgnoreCase))
        ?? throw new DebugApiException($"Nutzer '{key}' nicht gefunden");

    public void Dispose()
    {
        listener.Close();
        try
        {
            loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
    }
}
