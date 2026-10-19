using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Manager.Control;

/// <summary>
/// Package M12 (Packages 69, 151 ff.): changes a server's settings and icon through the service session; the server
/// checks the values (ServerLimitsRule) and its refusal comes back as text. Each change is in the manager's audit log.
/// </summary>
internal sealed class SettingsCommands(ServerConnections connections, ManagerDbContext database, AuditLog audit)
{
    /// <summary>The settings the server has now, null while it is not connected.</summary>
    public SettingsInput? Current(string serverId) => connections.LinkOf(serverId)?.Read(mirror => From(mirror.Settings), null);

    /// <summary>AC1, AC4: saves <paramref name="input"/>; returns null or why the server refused it.</summary>
    public async Task<string?> SaveAsync(string serverId, SettingsInput input, string actor, CancellationToken cancellation = default)
    {
        if (connections.LinkOf(serverId) is not { Status: ServerLinkStatus.Connected } link) return Strings.Link_NotConnected;
        var before = link.Read(mirror => mirror.Settings, null);
        if (before is null) return Strings.Link_NotConnected;
        string? password = input.RemovePassword ? "" : input.NewPassword.Length > 0 ? input.NewPassword : null;
        var limits = new ServerLimits(input.MaxUsers, input.LogDays, input.LogRotateDaily, input.AutoRestart, input.AutoRestartTime,
            before.Limits?.AfkChannelId, before.Limits?.AfkMinutes ?? 0);
        var refused = await link.RequestAsync(new UpdateServerSettings(input.Name, input.WelcomeText, password, limits), message => message is ServerSettingsChanged, cancellation);
        if (refused is not null) return ServerTexts.Refusal(refused);
        await database.Servers.Where(server => server.Id == serverId).ExecuteUpdateAsync(setters => setters.SetProperty(server => server.Name, input.Name.Trim()), cancellation);
        await audit.WriteAsync(actor, AuditActions.ServerSettingsChanged, input.Name.Trim(), serverId, Changes(From(before), input, password), cancellation);
        return null;
    }

    /// <summary>AC3: sets <paramref name="png"/> as the icon (null removes it); returns null or why it was refused.</summary>
    public async Task<string?> SetIconAsync(string serverId, byte[]? png, string actor, CancellationToken cancellation = default)
    {
        if (png is not null && png.Length > ServerIconFormat.MaxBytes) return TextFormat.Of(Strings.Icon_TooLarge, ServerIconFormat.MaxBytes / 1024);
        if (png is not null && ServerIconFormat.Validate(png) is not null) return TextFormat.Of(Strings.Icon_Invalid, ServerIconFormat.MinSize, ServerIconFormat.MaxSize);
        if (connections.LinkOf(serverId) is not { Status: ServerLinkStatus.Connected } link) return Strings.Link_NotConnected;
        var refused = await link.RequestAsync(new SetServerIcon(png is null ? null : Convert.ToBase64String(png)), message => message is ServerSettingsChanged, cancellation);
        if (refused is not null) return ServerTexts.Refusal(refused);
        await audit.WriteAsync(actor, AuditActions.ServerIconChanged, link.Read(mirror => mirror.Settings.Name, ""), serverId, png is null ? "removed" : "set", cancellation);
        return null;
    }

    static SettingsInput From(ServerSettingsInfo settings) => new()
    {
        Name = settings.Name, WelcomeText = settings.WelcomeText, MaxUsers = settings.Limits?.MaxUsers ?? 0, LogDays = settings.Limits?.LogDays ?? 0,
        LogRotateDaily = settings.Limits?.LogRotateDaily ?? false, AutoRestart = settings.Limits?.AutoRestart ?? false,
        AutoRestartTime = settings.Limits?.AutoRestartTime ?? default,
    };

    /// <summary>AC4: the changed fields for the audit line; the password only as changed.</summary>
    static string Changes(SettingsInput before, SettingsInput after, string? password)
    {
        var changes = new List<string>();
        if (before.Name != after.Name.Trim()) changes.Add($"name '{before.Name}' -> '{after.Name.Trim()}'");
        if (before.WelcomeText != after.WelcomeText) changes.Add("welcome text changed");
        if (password is not null) changes.Add(password.Length == 0 ? "password removed" : "password changed");
        if (before.MaxUsers != after.MaxUsers) changes.Add($"max users {before.MaxUsers} -> {after.MaxUsers}");
        if (before.LogDays != after.LogDays) changes.Add($"keep logs {before.LogDays} -> {after.LogDays} days");
        if (before.LogRotateDaily != after.LogRotateDaily) changes.Add($"daily log file {(after.LogRotateDaily ? "on" : "off")}");
        if (before.AutoRestart != after.AutoRestart || before.AutoRestartTime != after.AutoRestartTime)
            changes.Add(after.AutoRestart ? $"automatic restart at {after.AutoRestartTime:HH\\:mm}" : "automatic restart off");
        return changes.Count > 0 ? string.Join(", ", changes) : "no change";
    }
}
