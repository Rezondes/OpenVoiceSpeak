using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The settings card on its own: when it holds changes, what it sends, and how it follows the server.</summary>
public sealed class ServerSettingsViewModelTests
{
    static readonly ServerLimits ReportedLimits = new(50, 30, true, false, new TimeOnly(4, 0));

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];

    sealed record Rig(ServerSettingsViewModel Card, ServerViewModel Server);

    /// <param name="limits">Limits the server has reported before the card is built; none by default.</param>
    Rig Create(ServerLimits? limits = null)
    {
        var server = FakeServers.Admin(sent, time);
        if (limits is not null) server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { Limits = limits }));
        return new Rig(new ServerSettingsViewModel(server), server);
    }

    /// <summary>The server reports new settings and the page tells the card about the state change.</summary>
    static void ApplyServerSettings(Rig rig, Func<ServerSettingsInfo, ServerSettingsInfo> change)
    {
        rig.Server.Apply(new ServerSettingsChanged(change(rig.Server.Mirror.Settings)));
        rig.Card.FollowServer();
    }

    [Fact]
    public void HasChanges_NothingEdited_False()
    {
        var rig = Create();

        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void HasChanges_NameEditedWithoutTheRight_False()
    {
        var rig = Create();
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.Speak, []);

        rig.Card.Name = "Neu";

        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void HasChanges_OnlyBlanksAroundTheName_False()
    {
        var rig = Create();

        rig.Card.Name = "  Gilde  ";

        Assert.False(rig.Card.HasChanges);
    }

    [Theory]
    [InlineData("Neu", "Hallo")]
    [InlineData("Gilde", "Neu")]
    public void HasChanges_NameOrWelcomeTextEdited_True(string name, string welcomeText)
    {
        var rig = Create();

        rig.Card.Name = name;
        rig.Card.WelcomeText = welcomeText;

        Assert.True(rig.Card.HasChanges);
    }

    [Theory]
    [InlineData("geheim", false)]
    [InlineData("", true)]
    public void HasChanges_PasswordTypedOrRemovalChecked_True(string newPassword, bool removePassword)
    {
        var rig = Create();

        rig.Card.NewPassword = newPassword;
        rig.Card.RemovePassword = removePassword;

        Assert.True(rig.Card.HasChanges);
    }

    [Fact]
    public void FollowServer_ServerLosesItsPassword_HasPasswordFollows()
    {
        var rig = Create();
        Assert.True(rig.Card.HasPassword);

        ApplyServerSettings(rig, settings => settings with { HasPassword = false });

        Assert.False(rig.Card.HasPassword);
    }

    [Fact]
    public void FollowServer_LimitsArriveLater_HasLimitsAndShowsTheirValues()
    {
        var rig = Create();
        Assert.False(rig.Card.HasLimits);

        ApplyServerSettings(rig, settings => settings with { Limits = ReportedLimits });

        Assert.True(rig.Card.HasLimits);
        Assert.Equal(50m, rig.Card.MaxUsers);
        Assert.Equal(new TimeSpan(4, 0, 0), rig.Card.AutoRestartTime);
    }

    [Fact]
    public void FollowServer_OtherAdminRenamesTheServer_AnUntouchedNameFollows()
    {
        var rig = Create();

        ApplyServerSettings(rig, settings => settings with { Name = "Anderer Name" });

        Assert.Equal("Anderer Name", rig.Card.Name);
        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void FollowServer_OtherAdminRenamesTheServer_AnEditedNameStays()
    {
        var rig = Create();
        rig.Card.Name = "Eigener Name";

        ApplyServerSettings(rig, settings => settings with { Name = "Anderer Name" });

        Assert.Equal("Eigener Name", rig.Card.Name);
        Assert.True(rig.Card.HasChanges);
    }

    [Fact]
    public void FollowServer_NothingChanged_KeepsTheEdits()
    {
        var rig = Create();
        rig.Card.Name = "Eigener Name";

        rig.Card.FollowServer();

        Assert.Equal("Eigener Name", rig.Card.Name);
    }

    [Fact]
    public void Reload_EditsAndAnErrorPresent_ShowsTheServersValuesAndClearsTheError()
    {
        var rig = Create();
        rig.Card.Name = "";
        rig.Card.WelcomeText = "Neu";
        rig.Card.NewPassword = "geheim";
        rig.Card.RemovePassword = true;
        Assert.Null(rig.Card.Send());
        Assert.NotNull(rig.Card.Error);

        rig.Card.Reload();

        Assert.Equal(("Gilde", "Hallo", "", false, null), (rig.Card.Name, rig.Card.WelcomeText, rig.Card.NewPassword, rig.Card.RemovePassword, rig.Card.Error));
        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void Send_PasswordTyped_SendsItAndEmptiesThePasswordField()
    {
        var rig = Create();
        rig.Card.NewPassword = "geheim";

        var pending = rig.Card.Send();

        Assert.NotNull(pending);
        Assert.Equal(new UpdateServerSettings("Gilde", "Hallo", "geheim"), Assert.Single(sent) with { RequestId = null });
        Assert.Equal("", rig.Card.NewPassword);
        Assert.False(rig.Card.RemovePassword);
    }

    [Theory]
    [InlineData("", false, null)]
    [InlineData("neu", false, "neu")]
    [InlineData("egal", true, "")]
    public void Save_PasswordFieldsSet_SendsTheTypedPasswordTheEmptyOneToRemoveItOrNone(string newPassword, bool removePassword, string? expected)
    {
        var rig = Create();
        rig.Card.Name = "Neu";
        rig.Card.NewPassword = newPassword;
        rig.Card.RemovePassword = removePassword;

        rig.Card.SaveCommand.Execute(null);

        Assert.Equal(new UpdateServerSettings("Neu", "Hallo", expected), Assert.Single(sent) with { RequestId = null });
    }

    /// <summary>The limits travel with name, welcome text and password.</summary>
    [Fact]
    public void Save_EveryLimitEdited_SendsTheEditedLimitsWithNameAndWelcomeTextAndNoPassword()
    {
        var rig = Create(ReportedLimits);
        rig.Card.MaxUsers = 12;
        rig.Card.LogDays = 0;
        rig.Card.LogRotateDaily = false;
        rig.Card.AutoRestart = true;
        rig.Card.AutoRestartTime = new TimeSpan(3, 30, 0);

        rig.Card.SaveCommand.Execute(null);

        Assert.Equal(new UpdateServerSettings("Gilde", "Hallo", null, new ServerLimits(12, 0, false, true, new TimeOnly(3, 30))),
            Assert.Single(sent) with { RequestId = null });
    }

    [Fact]
    public void Save_LimitsNeverReported_SendsNoLimits()
    {
        var rig = Create();

        rig.Card.SaveCommand.Execute(null);

        Assert.Null(Assert.IsType<UpdateServerSettings>(Assert.Single(sent)).Limits);
    }

    [Fact]
    public void Save_NameWithAZeroWidthSpace_ShowsTheNameErrorAndSendsNothing()
    {
        var rig = Create();
        rig.Card.Name = "Server" + (char)0x200B;

        rig.Card.SaveCommand.Execute(null);

        Assert.Equal(Strings.Dlg_NameInvalid, rig.Card.Error);
        Assert.Empty(sent);
    }

    [Fact]
    public void Save_WelcomeTextWithADirectionIsolate_ShowsTheTextErrorAndSendsNothing()
    {
        var rig = Create();
        rig.Card.WelcomeText = "a" + (char)0x2066 + "b";

        rig.Card.SaveCommand.Execute(null);

        Assert.Equal(Strings.Dlg_TextInvalid, rig.Card.Error);
        Assert.Empty(sent);
    }

    [Fact]
    public void Save_PasswordTooLong_ShowsTheErrorSendsNothingAndKeepsThePasswordForCorrecting()
    {
        var rig = Create();
        var tooLong = new string('p', ProtocolLimits.MaxPasswordLength + 1);
        rig.Card.NewPassword = tooLong;

        rig.Card.SaveCommand.Execute(null);

        Assert.Equal(Strings.Ui_PasswordTooLong, rig.Card.Error);
        Assert.Equal(tooLong, rig.Card.NewPassword);
        Assert.Empty(sent);
    }

    [Fact]
    public void Save_PasswordCorrectedAfterAnError_ClearsTheErrorAndSendsTheSettings()
    {
        var rig = Create();
        rig.Card.NewPassword = new string('p', ProtocolLimits.MaxPasswordLength + 1);
        rig.Card.SaveCommand.Execute(null);
        rig.Card.NewPassword = "kurz";

        rig.Card.SaveCommand.Execute(null);

        Assert.Null(rig.Card.Error);
        Assert.Equal(new UpdateServerSettings("Gilde", "Hallo", "kurz"), Assert.Single(sent) with { RequestId = null });
    }

    [Fact]
    public void Send_NameInvalid_ShowsTheErrorAndKeepsThePasswordFields()
    {
        var rig = Create();
        rig.Card.Name = "";
        rig.Card.NewPassword = "geheim";

        var pending = rig.Card.Send();

        Assert.Null(pending);
        Assert.Empty(sent);
        Assert.Equal(Strings.Dlg_NameInvalid, rig.Card.Error);
        Assert.Equal("geheim", rig.Card.NewPassword);
    }

    [Fact]
    public void HasChanges_LimitEdited_True()
    {
        var rig = Create(ReportedLimits);

        rig.Card.MaxUsers = 12;

        Assert.True(rig.Card.HasChanges);
    }

    [Fact]
    public void FollowServer_OtherAdminChangesLimits_UntouchedLimitsFollow()
    {
        var rig = Create(ReportedLimits);

        ApplyServerSettings(rig, settings => settings with { Limits = ReportedLimits with { MaxUsers = 80 } });

        Assert.Equal(80m, rig.Card.MaxUsers);
        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void FollowServer_OtherAdminChangesLimits_EditedLimitsStay()
    {
        var rig = Create(ReportedLimits);
        rig.Card.LogDays = 5;

        ApplyServerSettings(rig, settings => settings with { Limits = ReportedLimits with { MaxUsers = 80 } });

        Assert.Equal(5m, rig.Card.LogDays);
        Assert.True(rig.Card.HasChanges);
    }

    [Fact]
    public void Reload_LimitEditedAndServerNoLongerReportsLimits_HasNoLimitsAndHasNoChanges()
    {
        var rig = Create(ReportedLimits);
        rig.Card.MaxUsers = 80;
        ApplyServerSettings(rig, settings => settings with { Limits = null });
        Assert.True(rig.Card.HasLimits);
        Assert.True(rig.Card.HasChanges);

        rig.Card.Reload();

        Assert.False(rig.Card.HasLimits);
        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void Reload_ServerNoLongerReportsLimits_RaisesHasLimits()
    {
        var rig = Create(ReportedLimits);
        ApplyServerSettings(rig, settings => settings with { Limits = null });
        Assert.True(rig.Card.HasLimits);
        var raised = new List<string?>();
        rig.Card.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        rig.Card.Reload();

        Assert.Contains(nameof(ServerSettingsViewModel.HasLimits), raised);
    }

    [Fact]
    public void Reload_LimitEdited_ShowsTheServersLimitsAgain()
    {
        var rig = Create(ReportedLimits);
        rig.Card.MaxUsers = 12;

        rig.Card.Reload();

        Assert.Equal(50m, rig.Card.MaxUsers);
        Assert.False(rig.Card.HasChanges);
    }

    [Fact]
    public void Send_NumberFieldEmptied_SendsTheLoadedValue()
    {
        var rig = Create(ReportedLimits);
        rig.Card.MaxUsers = null;

        rig.Card.Send();

        Assert.Equal(ReportedLimits, ((UpdateServerSettings)Assert.Single(sent)).Limits);
    }

    [Fact]
    public async Task Place_NothingEdited_NoChangesAndSavingSucceedsWithoutSending()
    {
        var rig = Create();

        Assert.False(rig.Card.Place.HasChanges());
        Assert.True(await HangGuard.Bound(rig.Card.Place.SaveAsync()));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Place_NameEdited_HasChangesAndSavingSendsTheSettingsAndWaitsForTheServer()
    {
        var rig = Create();
        rig.Card.Name = "Neu";
        Assert.True(rig.Card.Place.HasChanges());

        var saving = rig.Card.Place.SaveAsync();

        Assert.Equal("Neu", Assert.IsType<UpdateServerSettings>(Assert.Single(sent)).Name);
        Assert.False(saving.IsCompleted);
        ApplyServerSettings(rig, settings => settings with { Name = "Neu" });
        Assert.True(await HangGuard.Bound(saving));
        Assert.False(rig.Card.Place.HasChanges());
    }

    [Fact]
    public async Task Place_NameBlank_HasChangesButSavingFailsWithoutSending()
    {
        var rig = Create();
        rig.Card.Name = "   ";

        Assert.True(rig.Card.Place.HasChanges());
        Assert.False(await HangGuard.Bound(rig.Card.Place.SaveAsync()));
        Assert.Empty(sent);
    }

    [Fact]
    public void PlaceDiscard_NameEdited_ServerNameBackAndNoChanges()
    {
        var rig = Create();
        rig.Card.Name = "Neu";

        rig.Card.Place.Discard();

        Assert.Equal("Gilde", rig.Card.Name);
        Assert.False(rig.Card.Place.HasChanges());
    }
}
