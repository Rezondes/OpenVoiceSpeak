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
using OVS.Shared.Protocol.Messages.Links;
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
        rig.Flow.CloseWithoutAsking();
        Assert.All(bindings, binding => Assert.Contains(binding, rig.Changes));
    }

    [AvaloniaFact]
    public async Task OpenSettings_FromTheAdministration_DetachesItFirst()
    {
        var rig = new PageFlowRig();
        var server = rig.ShowAdminServer();
        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());
        var admin = rig.Flow.AdminPage!;
        rig.Changes.Clear();

        rig.Flow.OpenSettings();
        var raised = new List<string?>();
        admin.PropertyChanged += (_, args) => raised.Add(args.PropertyName);
        FakeServers.SetOwnRightsAndGroups(server, Permission.All, [WellKnownGroups.Admin]);

        Assert.Empty(raised);
        Assert.Null(rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsSettingsPage);
        Assert.InRange(rig.Changes.IndexOf(nameof(PageFlowViewModel.AdminPage)), 0, rig.Changes.LastIndexOf(nameof(PageFlowViewModel.SettingsPage)) - 1);
    }

    [AvaloniaFact]
    public void CloseSettings_AfterTheMeterStarted_StopsItWithTheHandlerItStartedWith()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();

        rig.Flow.CloseWithoutAsking();

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
        await HangGuard.BoundAsync(rig.Flow.OpenSettingsCommand.ExecuteAsync(null));

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

        await HangGuard.BoundAsync(page.ApplySaved!());

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

        rig.Flow.CloseWithoutAsking();
        rig.RunPosted();

        Assert.Equal(levelBefore, page.InputLevelDb);
        Assert.Null(rig.Flow.SettingsPage);
    }

    [AvaloniaFact]
    public void ClosePage_OnSettings_RestoresTheSavedSettingsInOrderAndClearsThePage()
    {
        var rig = new PageFlowRig();
        rig.OpenEditedSettings();

        rig.Flow.CloseWithoutAsking();

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

        rig.Flow.CloseWithoutAsking();
        rig.Changes.Clear();
        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());
        rig.AssertChangedBefore(nameof(PageFlowViewModel.AdminPage), nameof(PageFlowViewModel.Page));
    }

    [AvaloniaFact]
    public async Task ClosePage_SettingsAndAdministration_TheirPageChangesBeforeThePageKind()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        rig.Flow.OpenSettings();
        rig.Changes.Clear();
        rig.Flow.CloseWithoutAsking();
        rig.AssertChangedBefore(nameof(PageFlowViewModel.SettingsPage), nameof(PageFlowViewModel.Page));

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());
        rig.Changes.Clear();
        rig.Flow.CloseWithoutAsking();
        rig.AssertChangedBefore(nameof(PageFlowViewModel.AdminPage), nameof(PageFlowViewModel.Page));
    }

    [AvaloniaFact]
    public async Task ClosePage_OnHome_ChangesNothingAndAsksNothing()
    {
        var rig = new PageFlowRig();

        rig.Flow.CloseWithoutAsking();
        await HangGuard.BoundAsync(rig.Flow.ClosePageAsync());

        Assert.Empty(rig.Changes);
        Assert.Empty(rig.Calls);
        Assert.Equal(0, rig.Asked);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_NoServer_OpensNothing()
    {
        var rig = new PageFlowRig { ShownServer = null };

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());

        Assert.True(rig.Flow.IsHomePage);
        Assert.Null(rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_ServerWithoutTheAdminRight_OpensNothing()
    {
        var rig = new PageFlowRig();
        FakeServers.SetOwnRightsAndGroups(rig.ShowAdminServer(), Permission.Speak, [FakeServers.GuestGroup.Id]);

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());

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

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());

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

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());

        Assert.Same(page, rig.Flow.SettingsPage);
        Assert.Null(rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public async Task HasUnsavedChanges_SettingsOrAdminChanges_AdminOnlyIgnoresTheSettings(bool administration, bool edited, bool adminOnly, bool expected)
    {
        var rig = new PageFlowRig();
        if (administration)
        {
            rig.ShowAdminServer();
            await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());
            if (edited) rig.Flow.AdminPage!.ServerSettings.Name = "Neu";
        }
        else
        {
            rig.Flow.OpenSettings();
            if (edited) rig.Flow.SettingsPage!.VadThresholdDb = -40;
        }

        Assert.Equal(expected, rig.Flow.HasUnsavedChanges(adminOnly));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void HasUnsavedChanges_NoPageOpen_IsFalse(bool adminOnly)
    {
        var rig = new PageFlowRig();

        Assert.False(rig.Flow.HasUnsavedChanges(adminOnly));
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_NoPageOpen_AsksNothingAndReturnsTrue()
    {
        var rig = new PageFlowRig();

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
        Assert.Equal(0, rig.Asked);
    }

    [AvaloniaFact]
    public void ClosePage_SettingsClosedTwice_RunsTheTeardownOnce()
    {
        var rig = new PageFlowRig();
        rig.OpenEditedSettings();

        rig.Flow.CloseWithoutAsking();
        rig.Flow.CloseWithoutAsking();

        Assert.Equal(["applyLive", "setKeyBindings", "showAppearance", "setSelfTest False", "stopLevelMeter"], rig.Calls);
        Assert.True(rig.Flow.IsHomePage);
    }

    [AvaloniaFact]
    public async Task ClosePage_AdministrationClosedTwice_SecondCloseChangesNothing()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        await rig.OpenEditedAdminAsync();
        rig.Flow.CloseWithoutAsking();
        rig.Changes.Clear();

        rig.Flow.CloseWithoutAsking();

        Assert.Empty(rig.Changes);
        Assert.Null(rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsHomePage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_SaveChosenAndTheSecondPlaceFails_ReturnsFalseKeepsTheAdministrationAndSendsNoLaterPlace()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer(request => request is SetChannelLinks ? new Error(request.RequestId, ReasonCodes.PermissionDenied) : null);
        var admin = await rig.OpenEditedAdminAsync();
        admin.Links.UnlinkAll();
        rig.Answers.Enqueue(LeaveChoice.Save);

        Assert.False(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
        Assert.Single(rig.Sent.OfType<SetChannelLinks>());
        Assert.DoesNotContain(rig.Sent, request => request is UpdateServerSettings);
        Assert.Same(admin, rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsAdminPage);
    }

    [AvaloniaFact]
    public void ClosePage_SettingsTeardownThrows_PropagatesAndTheSettingsPageStaysShown()
    {
        var rig = new PageFlowRig();
        var page = rig.OpenEditedSettings();
        rig.ApplyLiveThrows = true;

        Assert.Throws<InvalidOperationException>(rig.Flow.CloseWithoutAsking);
        Assert.Same(page, rig.Flow.SettingsPage);
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_SettingsHaveChangesAndUserSaves_SavesTheSettingsThenOpensTheAdministration()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        rig.OpenEditedSettings();
        rig.Answers.Enqueue(LeaveChoice.Save);

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());

        Assert.Equal(["applySaved", "applyLive", "setKeyBindings", "showAppearance", "setSelfTest False", "stopLevelMeter"], rig.Calls);
        Assert.Equal(-40f, rig.AppliedSaved.Single().VadThresholdDb);
        Assert.NotNull(rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsAdminPage);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_SettingsHaveChangesAndUserDiscards_RunsTheSettingsTeardownBeforeTheAdministrationIsShown()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        rig.OpenEditedSettings();
        rig.Answers.Enqueue(LeaveChoice.Discard);
        List<string>? callsWhenShown = null;
        rig.Flow.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PageFlowViewModel.AdminPage) && rig.Flow.AdminPage is not null) callsWhenShown ??= [.. rig.Calls];
        };

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());

        Assert.Equal(["applyLive", "setKeyBindings", "showAppearance", "setSelfTest False", "stopLevelMeter"], callsWhenShown);
        Assert.Empty(rig.AppliedSaved);
        Assert.True(rig.Flow.IsAdminPage);
    }

    [AvaloniaFact]
    public async Task SettingsCloseRequested_AfterTheAdministrationOpened_RunsTheSettingsTeardownAndKeepsTheAdministration()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        rig.Flow.OpenSettings();
        var page = rig.Flow.SettingsPage!;
        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());
        var admin = rig.Flow.AdminPage;
        rig.Calls.Clear();

        await HangGuard.BoundAsync(page.SaveAndCloseCommand.ExecuteAsync(null)); // a late "Speichern" of the page no longer shown

        Assert.Equal(["applySaved", "applyLive", "setKeyBindings", "showAppearance", "setSelfTest False", "stopLevelMeter"], rig.Calls);
        Assert.NotNull(admin);
        Assert.Same(admin, rig.Flow.AdminPage);
        Assert.True(rig.Flow.IsAdminPage);
    }

    [AvaloniaFact]
    public async Task SettingsCloseRequested_AfterANewerSettingsPageOpened_RunsTheTeardownAndKeepsTheNewerPage()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();
        var page1 = rig.Flow.SettingsPage!;
        rig.Flow.CloseWithoutAsking();
        rig.Flow.OpenSettings();
        var page2 = rig.Flow.SettingsPage;
        rig.Calls.Clear();

        await HangGuard.BoundAsync(page1.SaveAndCloseCommand.ExecuteAsync(null));

        Assert.Equal(["applySaved", "applyLive", "setKeyBindings", "showAppearance", "setSelfTest False", "stopLevelMeter"], rig.Calls);
        Assert.NotNull(page2);
        Assert.Same(page2, rig.Flow.SettingsPage);
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_AdminOnlyAndTheAdministrationClosedWhileAsked_SavesNothingAndReturnsTrue()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        await rig.OpenEditedAdminAsync();
        rig.Answers.Enqueue(LeaveChoice.Save);
        rig.WhileAsked = () =>
        {
            rig.Flow.OpenSettings();
            rig.Flow.SettingsPage!.VadThresholdDb = -40;
        };

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync(adminOnly: true)));
        Assert.Equal(1, rig.Asked);
        Assert.Empty(rig.AppliedSaved);
        Assert.DoesNotContain(rig.Sent, request => request is UpdateServerSettings);
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_NoChanges_AsksNothing()
    {
        var rig = new PageFlowRig();
        rig.Flow.OpenSettings();

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
        Assert.Equal(0, rig.Asked);
    }

    [AvaloniaFact]
    public async Task OpenAdminAsync_AdminDialogsSetAfterTheFlowWasBuilt_TheAdministrationAsksThroughThem()
    {
        var rig = new PageFlowRig();
        var asked = 0;
        rig.Dialogs = new DialogCallbacks { Admin = new() { AskLeave = () => { asked++; return Task.FromResult(LeaveChoice.Stay); } } };
        rig.ShowAdminServer();

        await HangGuard.BoundAsync(rig.Flow.OpenAdminAsync());
        var stays = !await HangGuard.BoundAsync(rig.Flow.AdminPage!.ConfirmLeaveAsync(new AdminPlace(() => true, () => Task.FromResult(false), () => { })));

        Assert.True(stays);
        Assert.Equal((1, 0), (asked, rig.Asked));
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_NoAskLeaveDialog_LeavesWithoutSaving()
    {
        var rig = new PageFlowRig { Dialogs = new DialogCallbacks() };
        rig.OpenEditedSettings();

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
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

        Assert.Equal(expected, await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
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

        Assert.False(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
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

        Assert.False(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
        Assert.Single(rig.Sent.OfType<UpdateGroup>());
        Assert.DoesNotContain(rig.Sent, request => request is UpdateServerSettings);
        Assert.Same(admin, rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_AdminOnlyWithSettingsChanged_AsksNothingAndSavesNothing()
    {
        var rig = new PageFlowRig();
        rig.OpenEditedSettings();

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync(adminOnly: true)));
        Assert.Equal(0, rig.Asked);
        Assert.Empty(rig.AppliedSaved);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_AskLeaveThrows_Propagates()
    {
        var rig = new PageFlowRig { AskThrows = true };
        rig.OpenEditedSettings();

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
        Assert.True(rig.Flow.IsSettingsPage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_SaveChosenAndThePageClosedWhileAsked_SavesNothingAndReturnsTrue()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        await rig.OpenEditedAdminAsync();
        rig.Answers.Enqueue(LeaveChoice.Save);
        rig.WhileAsked = rig.Flow.CloseWithoutAsking;

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync()));
        Assert.DoesNotContain(rig.Sent, request => request is UpdateServerSettings);
        Assert.True(rig.Flow.IsHomePage);
    }

    [AvaloniaFact]
    public async Task ConfirmLeaveAsync_SaveChosenAndAnUneditedPageShownWhileAsked_SavesNothing()
    {
        var rig = new PageFlowRig();
        rig.ShowAdminServer();
        await rig.OpenEditedAdminAsync();
        int sentBefore = rig.Sent.Count;
        rig.Answers.Enqueue(LeaveChoice.Save);
        rig.WhileAsked = rig.Flow.OpenSettings;

        Assert.True(await HangGuard.BoundAsync(rig.Flow.ConfirmLeaveAsync(adminOnly: false)));
        Assert.Empty(rig.AppliedSaved);
        Assert.Equal(sentBefore, rig.Sent.Count);
    }

    [AvaloniaFact]
    public void ShownPage_Settings_SettingsPageIsItAndAdminPageIsNull()
    {
        var rig = new PageFlowRig();

        rig.Flow.OpenSettings();

        Assert.NotNull(rig.Flow.ShownPage);
        Assert.Same(rig.Flow.ShownPage, rig.Flow.SettingsPage);
        Assert.Null(rig.Flow.AdminPage);
    }

    [AvaloniaFact]
    public void ShownPage_Changed_TellsTheSettingsPageAndAdminPageBindings()
    {
        var rig = new PageFlowRig();
        string[] bindings = [nameof(PageFlowViewModel.ShownPage), nameof(PageFlowViewModel.SettingsPage), nameof(PageFlowViewModel.AdminPage)];

        rig.Flow.OpenSettings();
        Assert.All(bindings, binding => Assert.Contains(binding, rig.Changes));

        rig.Changes.Clear();
        rig.Flow.CloseWithoutAsking();
        Assert.All(bindings, binding => Assert.Contains(binding, rig.Changes));
    }
}
