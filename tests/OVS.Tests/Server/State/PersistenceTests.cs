using System.IO.Compression;
using OVS.Server.Data;
using OVS.Server.Tls;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>How the request handlers save: synchronously before the broadcast, and what a backup takes along.</summary>
public sealed class PersistenceTests
{
    readonly ClientIdentity admin = ClientIdentity.Create();

    [Fact]
    public void CreateChannel_SaveFails_AnsweredInvalidValueWithoutBroadcast()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var session = harness.Admit("admin", admin.Fingerprint);
        var guest = harness.Admit("gast");
        StateHarness.Drain(session);
        StateHarness.Drain(guest);
        harness.State.BeforeSave = () => throw new IOException("Datei gesperrt");

        harness.Handle(session, new CreateChannel("Raid", "") { RequestId = "c" });

        Assert.Equal(new Error("c", ReasonCodes.InvalidValue), Assert.Single(StateHarness.Drain(session)));
        Assert.Empty(StateHarness.Drain(guest));
    }

    [Fact]
    public void CreateBackup_SavePending_ArchiveHoldsThePendingChange()
    {
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        File.WriteAllBytes(Path.Combine(harness.DataDir, ServerCertificate.FileName), [1]); // the archive takes the certificate file along
        var session = harness.Admit("admin", admin.Fingerprint);
        var newcomer = ClientIdentity.Create();
        harness.Admit("neu", newcomer.Fingerprint); // a login is saved debounced, so only the pending save holds it

        harness.Handle(session, new CreateBackup { RequestId = "b" });

        var backups = new BackupStore(harness.DataDir, harness.Time);
        using var archive = ZipFile.OpenRead(backups.Find(Assert.Single(backups.List()).FileName)!);
        using var data = new StreamReader(archive.GetEntry(DataStore.FileName)!.Open());
        Assert.Contains(newcomer.Fingerprint, data.ReadToEnd());
    }
}
