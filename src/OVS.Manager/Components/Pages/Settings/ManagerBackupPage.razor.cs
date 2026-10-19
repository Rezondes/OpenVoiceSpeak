using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Localization;

namespace OVS.Manager.Components.Pages.Settings;

/// <summary>Package M33 (AC1, AC4): the admin downloads the manager's backup with a password typed twice and sets its schedule.</summary>
sealed partial class ManagerBackupPage
{
    [Inject]
    ManagerBackupSchedule Schedule { get; set; } = null!;

    [Inject]
    ManagerOptions Options { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromForm(FormName = "manager-backup-schedule")]
    ScheduleInput? Model { get; set; }

    /// <summary>Why the last download was refused, from the address.</summary>
    [SupplyParameterFromQuery(Name = "error")]
    string? Error { get; set; }

    bool HasPassword => Options.BackupPassword is not null;

    string? Result { get; set; }

    bool Failed { get; set; }

    string ResultClass => Failed ? "form-error" : "form-ok";

    /// <summary>Loads the schedule.</summary>
    protected override async Task OnInitializedAsync() =>
        Model ??= new ScheduleInput { Interval = await Schedule.IntervalAsync(), Keep = await Schedule.KeepAsync() };

    async Task SaveAsync()
    {
        var refused = await Schedule.SaveAsync(Model!.Interval, Model.Keep, HttpContext.User.Identity?.Name ?? "");
        (Failed, Result) = (refused is not null, refused ?? Strings.Settings_Saved);
    }

    /// <summary>The fields of the schedule.</summary>
    sealed class ScheduleInput
    {
        public BackupInterval Interval { get; set; }

        public int Keep { get; set; } = 7;
    }
}
