using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Shared.Protocol;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>The admin uploads a server's backup, sees its version and identity, picks name and port, and gets a new server from it.</summary>
sealed partial class ServerFromBackupPage
{
    [Inject]
    ServerFromBackup FromBackup { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The backup read, only one the manager knows.</summary>
    ServerBackupFile? Backup { get; set; }

    string Name { get; set; } = "";

    int Port { get; set; } = PortPool.First;

    /// <summary>The warning that a managed server has the backup's certificate, null when none has.</summary>
    string? SameIdentity { get; set; }

    string? Error { get; set; }

    bool Running { get; set; }

    string CreatedText => Backup is { } backup ? TimeZoneInfo.ConvertTime(backup.CreatedAt, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture) : "";

    async Task ReadAsync(InputFileChangeEventArgs change)
    {
        (Backup, SameIdentity, Error) = (null, null, null);
        if (change.File.Size > ProtocolLimits.MaxBackupUploadBytes)
        {
            Error = TextFormat.Of(Strings.FromBackup_TooLarge, ProtocolLimits.MaxBackupUploadBytes / 1024 / 1024);
            return;
        }
        using var bytes = new MemoryStream();
        await using (var stream = change.File.OpenReadStream(ProtocolLimits.MaxBackupUploadBytes)) await stream.CopyToAsync(bytes);
        var backup = ServerBackupFile.Read(bytes.ToArray());
        if (backup is null)
        {
            Error = Strings.FromBackup_Unreadable;
            return;
        }
        if (!backup.IsKnown)
        {
            Error = TextFormat.Of(Strings.FromBackup_VersionUnknown, backup.ServerVersion.Length > 0 ? backup.ServerVersion : "-");
            return;
        }
        Backup = backup;
        Name = backup.Name;
        Port = PortPool.FindFree(await Database.Servers.AsNoTracking().Select(server => server.Port).ToListAsync()) ?? PortPool.First;
        var same = await FromBackup.SameIdentityAsync(backup.Fingerprint);
        SameIdentity = same.Count == 0 ? null : TextFormat.Of(Strings.FromBackup_SameIdentity, string.Join(", ", same));
    }

    async Task CreateAsync()
    {
        if (Backup is not { } backup) return;
        Running = true;
        try
        {
            var created = await FromBackup.CreateAsync(backup, Name, Port, (await Authentication).User.Identity?.Name ?? "");
            if (created.Error is null)
            {
                Navigation.NavigateTo($"/servers/{created.ServerId}");
                return;
            }
            Error = TextFormat.Of(Strings.FromBackup_Failed, Name, created.Error);
        }
        catch (ServerRequestException exception)
        {
            Error = ServerTexts.Problem(exception.Problem);
        }
        catch (DockerCallException)
        {
            Error = Strings.Docker_CallFailed;
        }
        finally
        {
            Running = false;
        }
    }
}
