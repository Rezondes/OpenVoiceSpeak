using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using OVS.Manager.Control;
using OVS.Manager.Localization;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M15 (AC1, AC2): a server's backups with their marks; made, downloaded, uploaded, restored after a warning and
/// deleted after a second click; and the server's backup schedule.
/// </summary>
sealed partial class ServerBackupPage
{
    /// <summary>The largest upload the page takes; the server's own quota decides below it.</summary>
    const long MaxUpload = 2L * 1024 * 1024 * 1024;

    static readonly DayOfWeek[] DayList = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    [Inject]
    BackupCommands Commands { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The backups of the last load.</summary>
    IReadOnlyList<BackupInfo> Backups { get; set; } = [];

    /// <summary>The marks of the last load.</summary>
    IReadOnlyDictionary<string, BackupMark> Marks { get; set; } = new Dictionary<string, BackupMark>();

    /// <summary>The schedule in the form.</summary>
    BackupSchedule Schedule { get; set; } = new();

    /// <summary>The action and backup that wait for the second click.</summary>
    PendingConfirm? Confirm { get; set; }

    /// <summary>What went well, null before an action.</summary>
    string? Message { get; set; }

    /// <summary>Why the last action failed, null when it did not.</summary>
    string? Error { get; set; }

    static IReadOnlyList<DayOfWeek> Days => DayList;

    /// <summary>Loads the backups and the schedule.</summary>
    protected override async Task OnInitializedAsync()
    {
        Schedule = await Commands.ScheduleAsync(Id);
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        var page = await Commands.ListAsync(Id);
        Error = page.Error;
        Backups = page.Backups;
        Marks = page.Marks;
    }

    async Task CreateAsync() => await AfterAsync((await Commands.CreateAsync(Id, await ActorAsync())).Error);

    async Task UploadAsync(InputFileChangeEventArgs change)
    {
        await using var stream = change.File.OpenReadStream(MaxUpload);
        await AfterAsync((await Commands.UploadAsync(Id, stream, await ActorAsync())).Error);
    }

    Action Ask(BackupInfo backup, string action) => () => Confirm = new PendingConfirm(action, backup.FileName);

    Func<Task> Restore(BackupInfo backup) => async () => await AfterAsync(await Commands.RestoreAsync(Id, backup.FileName, await ActorAsync()), reload: false);

    Func<Task> Delete(BackupInfo backup) => async () => await AfterAsync(await Commands.DeleteAsync(Id, backup.FileName, await ActorAsync()));

    /// <summary>The radio button of <paramref name="interval"/> sets it in the schedule.</summary>
    Action<ChangeEventArgs> Pick(BackupInterval interval) => _ => Schedule.Interval = interval;

    async Task SaveScheduleAsync()
    {
        Schedule.ServerId = Id;
        await Commands.SaveScheduleAsync(Schedule, await ActorAsync(), Time);
        Message = Strings.Settings_Saved;
    }

    async Task AfterAsync(string? refusal, bool reload = true)
    {
        Confirm = null;
        Error = refusal;
        Message = refusal is null ? Strings.Settings_Saved : null;
        if (refusal is null && reload) await LoadAsync();
    }

    string DownloadLink(BackupInfo backup) => $"/servers/{Uri.EscapeDataString(Id)}/backups/download?file={Uri.EscapeDataString(backup.FileName)}";

    string MarkText(BackupInfo backup) => Marks.GetValueOrDefault(backup.FileName) switch
    {
        { Kind: BackupKind.Scheduled } => Strings.Backups_Scheduled,
        { Note: { } note } => note,
        _ => Strings.Backups_Manual,
    };

    string Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture);

    static string Size(long bytes) => TextFormat.Of(Strings.Backups_Megabytes, Math.Round(bytes / 1024d / 1024d, 1));

    static string DayName(DayOfWeek day) => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);

    async Task<string> ActorAsync() => (await Authentication).User.Identity?.Name ?? "";

    /// <summary>An action on a backup that waits for its second click.</summary>
    sealed record PendingConfirm(string Action, string FileName);
}
