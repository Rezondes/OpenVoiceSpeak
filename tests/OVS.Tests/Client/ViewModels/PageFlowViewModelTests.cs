using Avalonia.Headless.XUnit;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The page flow on a direct construction: what it hands to the pages, the order it works in and what it does with wrong or missing input.</summary>
[Collection(MotionCollection.Name)]
public sealed class PageFlowViewModelTests
{
    [AvaloniaFact]
    public void OpenSettings_FromHome_HandsTheKeyBindingEditorOfTheDialogsToThePage()
    {
        var rig = new PageFlowRig();

        rig.Flow.OpenSettings();

        Assert.Same(rig.Editor, rig.Flow.SettingsPage!.EditKeyBinding);
    }

    [AvaloniaFact]
    public void OpenSettings_NoKeyBindingEditorAndNoDeviceListYet_BuildsThePageWithoutThem()
    {
        var rig = new PageFlowRig { Dialogs = new DialogCallbacks(), KnownDevices = null };

        rig.Flow.OpenSettings();

        Assert.NotNull(rig.Flow.SettingsPage);
        Assert.Null(rig.Flow.SettingsPage.EditKeyBinding);
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaFact]
    public async Task AdminPageCloseButton_UnsavedChanges_AsksFirstAndTheAnswerDecides()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        var admin = await rig.OpenEditedAdminAsync();

        rig.Answers.Enqueue(LeaveChoice.Stay);
        admin.CloseCommand.Execute(null);
        Assert.Equal(1, rig.Asked);
        Assert.True(rig.Flow.IsAdminPage);

        rig.Answers.Enqueue(LeaveChoice.Discard);
        admin.CloseCommand.Execute(null);
        Assert.Equal(2, rig.Asked);
        Assert.True(rig.Flow.IsHomePage);
    }

    [AvaloniaFact]
    public void Page_Changed_TellsTheThreeIsPageBindings()
    {
        var rig = new PageFlowRig();
        string[] bindings = [nameof(PageFlowViewModel.IsHomePage), nameof(PageFlowViewModel.IsSettingsPage), nameof(PageFlowViewModel.IsAdminPage)];

        rig.Flow.OpenSettings();
        Assert.All(bindings, binding => Assert.Contains(binding, rig.Changes));

        rig.Changes.Clear();
        rig.Flow.ClosePage();
        Assert.All(bindings, binding => Assert.Contains(binding, rig.Changes));
    }

    [AvaloniaFact]
    public async Task OpenSettings_FromTheAdministration_DetachesItFirst()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        await HangGuard.Bound(rig.Flow.OpenAdminAsync());
        rig.Changes.Clear();

        rig.Flow.OpenSettings();

        Assert.Null(rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsSettingsPage);
        rig.AssertChangedBefore(nameof(PageFlowViewModel.AdminPage), nameof(PageFlowViewModel.SettingsPage));
    }

    [AvaloniaFact]
    public void CloseSettings_AfterTheMeterStarted_StopsItWithTheHandlerItStartedWith()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();

        rig.Flow.ClosePage();

        Assert.NotNull(rig.StartedHandler);
        Assert.Equal(rig.StartedHandler, rig.StoppedHandler);
    }

    [AvaloniaFact]
    public void OpenSettings_FromHome_StartsTheMeterOnceAndRefreshesTheDevicesLastOnTheShownPage()
    {
        var rig = new PageFlowRig();

        rig.Flow.OpenSettings();

        Assert.Equal(["startLevelMeter", "refreshDevices"], rig.Calls);
        Assert.Equal(Page.Settings, rig.PageAtRefresh);
    }

    [AvaloniaFact]
    public async Task OpenSettings_AlreadyOpen_AsksNothingAndStartsNothing()
    {
        var rig = new PageFlowRig();
        var page = rig.OpenEditedSettings();

        rig.Flow.OpenSettings();
        await HangGuard.Bound(rig.Flow.OpenSettingsCommand.ExecuteAsync(null));

        Assert.Equal(0, rig.Asked);
        Assert.Same(page, rig.Flow.SettingsPage);
        Assert.DoesNotContain("startLevelMeter", rig.Calls);
        Assert.DoesNotContain("refreshDevices", rig.Calls);
    }

    [AvaloniaFact]
    public void LivePreview_Settings_AppliesThemThenSetsTheKeyBindingsThenShowsTheLook()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();
        rig.Calls.Clear();
        var live = new ClientSettings { BackgroundOpacity = 0.5f, KeyBindings = [new KeyBinding(KeyAction.ToggleMute, new KeyChord(7))] };

        rig.Flow.SettingsPage!.LivePreview!(live);

        Assert.Equal(["applyLive", "setKeyBindings", "showAppearance"], rig.Calls);
        Assert.Same(live, rig.AppliedLive.Single());
        Assert.Same(live.KeyBindings, rig.AppliedKeyBindings.Single());
        Assert.Equal(BackgroundAppearance.From(live), rig.ShownAppearances.Single());
    }

    [AvaloniaFact]
    public async Task ApplySaved_PageEdited_PassesThePageOnTheSettingsInUseAtTheTime()
    {
        var rig = new PageFlowRig();
        var page = rig.OpenEditedSettings();
        rig.Settings = new ClientSettings { VadThresholdDb = -30, UserVolumes = new Dictionary<string, float> { ["anna"] = 0.5f } };

        await HangGuard.Bound(page.ApplySaved!());

        var saved = rig.AppliedSaved.Single();
        Assert.Equal(-40f, saved.VadThresholdDb);
        Assert.Same(rig.Settings.UserVolumes, saved.UserVolumes);
    }

    [AvaloniaFact]
    public void OnInputLevel_Level_ShowsTheLevelWithTheThresholdReadWithIt()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();
        var page = rig.Flow.SettingsPage!;
        var levelBefore = page.InputLevelDb;

        rig.StartedHandler!(-20f);
        rig.Threshold = -44f;
        Assert.Equal(levelBefore, page.InputLevelDb);
        rig.RunPosted();

        Assert.Equal(-20f, page.InputLevelDb);
        Assert.Equal(-33f, page.ThresholdInUseDb);
    }

    [AvaloniaFact]
    public void OnInputLevel_LevelArrivesAfterThePageClosed_IsDropped()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();
        var page = rig.Flow.SettingsPage!;
        var levelBefore = page.InputLevelDb;
        rig.StartedHandler!(-20f);

        rig.Flow.ClosePage();
        rig.RunPosted();

        Assert.Equal(levelBefore, page.InputLevelDb);
        Assert.Null(rig.Flow.SettingsPage);
    }

    [AvaloniaFact]
    public void ClosePage_OnSettings_RestoresTheSavedSettingsInOrderAndClearsThePage()
    {
        var rig = new PageFlowRig();
        rig.OpenEditedSettings();

        rig.Flow.ClosePage();

        Assert.Equal(["applyLive", "setKeyBindings", "showAppearance", "setSelfTest False", "stopLevelMeter"], rig.Calls);
        Assert.Same(rig.Settings, rig.AppliedLive.Last());
        Assert.Equal(rig.Settings.KeyBindings, rig.AppliedKeyBindings.Last());
        Assert.Null(rig.Flow.SettingsPage);
        Assert.True(rig.Flow.IsHomePage);
    }

    [AvaloniaFact]
    public async Task Pages_Opened_TheirPageChangesBeforeThePageKind()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();

        rig.Flow.OpenSettings();
        rig.AssertChangedBefore(nameof(PageFlowViewModel.SettingsPage), nameof(PageFlowViewModel.Page));

        rig.Flow.ClosePage();
        rig.Changes.Clear();
        await HangGuard.Bound(rig.Flow.OpenAdminAsync());
        rig.AssertChangedBefore(nameof(PageFlowViewModel.AdminPage), nameof(PageFlowViewModel.Page));
    }

    [AvaloniaFact]
    public async Task ClosePage_SettingsAndAdministration_TheirPageChangesBeforeThePageKind()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        rig.Flow.OpenSettings();
        rig.Changes.Clear();
        rig.Flow.ClosePage();
        rig.AssertChangedBefore(nameof(PageFlowViewModel.SettingsPage), nameof(PageFlowViewModel.Page));

        await HangGuard.Bound(rig.Flow.OpenAdminAsync());
        rig.Changes.Clear();
        rig.Flow.ClosePage();
        rig.AssertChangedBefore(nameof(PageFlowViewModel.AdminPage), nameof(PageFlowViewModel.Page));
    }

    [AvaloniaFact]
    public async Task ClosePage_OnHome_ChangesNothingAndAsksNothing()
    {
        var rig = new PageFlowRig();

        rig.Flow.ClosePage();
        await HangGuard.Bound(rig.Flow.ClosePageAsync());

        Assert.Empty(rig.Changes);
        Assert.Empty(rig.Calls);
        Assert.Equal(0, rig.Asked);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_NoServer_OpensNothing()
    {
        var rig = new PageFlowRig { ShownServer = null };

        await HangGuard.Bound(rig.Flow.OpenAdminAsync());

        Assert.True(rig.Flow.IsHomePage);
        Assert.Null(rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_ServerWithoutTheAdminRight_OpensNothing()
    {
        var rig = new PageFlowRig();
        FakeServers.SetOwnRightsAndGroups(rig.ShowAdminServer(), Permission.Speak, [FakeServers.GuestGroup.Id]);

        await HangGuard.Bound(rig.Flow.OpenAdminAsync());

        Assert.True(rig.Flow.IsHomePage);
        Assert.Null(rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_AlreadyOpen_AsksNothingAndRequestsNothingAgain()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        var admin = await rig.OpenEditedAdminAsync();
        int requested = rig.Sent.Count;

        await HangGuard.Bound(rig.Flow.OpenAdminAsync());

        Assert.Equal(0, rig.Asked);
        Assert.Equal(requested, rig.Sent.Count);
        Assert.Same(admin, rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_SettingsHaveChangesAndUserStays_KeepsTheSettingsPage()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        var page = rig.OpenEditedSettings();
        rig.Answers.Enqueue(LeaveChoice.Stay);

        await HangGuard.Bound(rig.Flow.OpenAdminAsync());

        Assert.Same(page, rig.Flow.SettingsPage);
        Assert.Null(rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    public void HasUnsavedChanges_SettingsOrAdminChanges_AdminOnlyIgnoresTheSettings(bool settingsEdited, bool adminEdited, bool adminOnly, bool expected)
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();
        if (settingsEdited) rig.Flow.SettingsPage!.VadThresholdDb = -40;
        rig.Flow.AdminPage = new AdminViewModel(rig.ShowAdminServer());
        if (adminEdited) rig.Flow.AdminPage.ServerSettings.Name = "Neu";

        Assert.Equal(expected, rig.Flow.HasUnsavedChanges(adminOnly));
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_NoChanges_AsksNothing()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();

        Assert.True(await HangGuard.Bound(rig.Flow.ConfirmLeaveAsync()));
        Assert.Equal(0, rig.Asked);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_NoAskLeaveDialog_LeavesWithoutSaving()
    {
        var rig = new PageFlowRig { Dialogs = new DialogCallbacks() };
        rig.OpenEditedSettings();

        Assert.True(await HangGuard.Bound(rig.Flow.ConfirmLeaveAsync()));
        Assert.Empty(rig.AppliedSaved);
    }

    [AvaloniaTheory]
    [InlineData(LeaveChoice.Stay, false, 0)]
    [InlineData(LeaveChoice.Discard, true, 0)]
    [InlineData(LeaveChoice.Save, true, 1)]
    public async Task ConfirmLeaveAsync_SettingsChanged_TheChoiceDecidesAndOnlySaveSaves(LeaveChoice choice, bool expected, int saves)
    {
        var rig = new PageFlowRig();
        rig.OpenEditedSettings();
        rig.Answers.Enqueue(choice);

        Assert.Equal(expected, await HangGuard.Bound(rig.Flow.ConfirmLeaveAsync()));
        Assert.Equal(1, rig.Asked);
        Assert.Equal(saves, rig.AppliedSaved.Count);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_SettingsSaveFails_ReturnsFalseAndAppliesNothing()
    {
        var chord = new KeyChord(5);
        var rig = new PageFlowRig
        {
            Settings = new ClientSettings
            {
                VadThresholdDb = -30,
                KeyBindings = [new KeyBinding(KeyAction.PushToTalk, chord), new KeyBinding(KeyAction.PushToTalk, chord)],
            },
        };
        rig.OpenEditedSettings();
        rig.Answers.Enqueue(LeaveChoice.Save);

        Assert.False(await HangGuard.Bound(rig.Flow.ConfirmLeaveAsync()));
        Assert.Empty(rig.AppliedSaved);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_FirstAdminPlaceFails_StopsThereAndReturnsFalse()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer(request => request is UpdateGroup ? new Error(request.RequestId, ReasonCodes.PermissionDenied) : null);
        var admin = await rig.OpenEditedAdminAsync();
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.First(group => group.IsEditable);
        admin.GroupList.SelectedGroup.Name = "Umbenannt";
        rig.Answers.Enqueue(LeaveChoice.Save);

        Assert.False(await HangGuard.Bound(rig.Flow.ConfirmLeaveAsync()));
        Assert.Single(rig.Sent.OfType<UpdateGroup>());
        Assert.DoesNotContain(rig.Sent, request => request is UpdateServerSettings);
        Assert.Same(admin, rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_AdminOnlyWithSettingsChanged_AsksNothingAndSavesNothing()
    {
        var rig = new PageFlowRig();
        rig.OpenEditedSettings();

        Assert.True(await HangGuard.Bound(rig.Flow.ConfirmLeaveAsync(adminOnly: true)));
        Assert.Equal(0, rig.Asked);
        Assert.Empty(rig.AppliedSaved);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_AskLeaveThrows_Propagates()
    {
        var rig = new PageFlowRig { AskThrows = true };
        rig.OpenEditedSettings();

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.Bound(rig.Flow.ConfirmLeaveAsync()));
        Assert.True(rig.Flow.IsSettingsPage);
    }
}
