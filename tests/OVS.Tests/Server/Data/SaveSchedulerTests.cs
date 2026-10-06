using OVS.Server;
using OVS.Server.Data;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

public sealed class SaveSchedulerTests
{
    static readonly TimeSpan Tick = TimeSpan.FromTicks(1);
    readonly object gate = new();
    readonly ManualTimeProvider time = new();
    readonly List<string> log = [];
    readonly SaveScheduler scheduler;
    int attempts;
    int saves;
    bool failing;
    Exception failure = new IOException("Datei gesperrt");
    bool? lastUnderGate;

    public SaveSchedulerTests() => scheduler = new SaveScheduler(gate, Save, time, log.Add);

    void Save()
    {
        attempts++;
        lastUnderGate = Monitor.IsEntered(gate);
        if (failing) throw failure;
        saves++;
    }

    void Locked(Action action)
    {
        lock (gate) action();
    }

    [Fact]
    public void PersistSoon_SeveralChanges_OneSaveAtSaveDelay()
    {
        Locked(() =>
        {
            scheduler.PersistSoon();
            scheduler.PersistSoon();
            scheduler.PersistSoon();
        });

        time.Advance(Limits.SaveDelay - Tick);
        Assert.Equal(0, saves);
        time.Advance(Tick);
        Assert.Equal(1, saves);
        time.Advance(Limits.SaveDelay * 3);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void PersistSoon_AgainBeforeSaveDelay_SavesAtTheFirstDeadline()
    {
        Locked(scheduler.PersistSoon);
        time.Advance(Limits.SaveDelay - Tick);

        Locked(scheduler.PersistSoon);
        time.Advance(Tick);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void PersistSoon_AfterATimerSave_SavesAgain()
    {
        Locked(scheduler.PersistSoon);
        time.Advance(Limits.SaveDelay);

        Locked(scheduler.PersistSoon);
        time.Advance(Limits.SaveDelay);

        Assert.Equal(2, saves);
    }

    [Fact]
    public void PersistSoon_TimerSave_RunsUnderTheGate()
    {
        Locked(scheduler.PersistSoon);

        time.Advance(Limits.SaveDelay);

        Assert.True(lastUnderGate);
    }

    [Fact]
    public void Persist_WhilePending_TakesThePendingSaveAlong()
    {
        Locked(scheduler.PersistSoon);

        Locked(scheduler.Persist);
        time.Advance(Limits.SaveDelay);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Persist_FailsWhilePending_ThrowsAndTheTimerSavesLater()
    {
        Locked(scheduler.PersistSoon);
        failing = true;

        Assert.Throws<IOException>(() => Locked(scheduler.Persist));
        failing = false;
        time.Advance(Limits.SaveDelay);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Persist_NoEarlierFailure_NotLogged()
    {
        Locked(scheduler.Persist);

        Assert.Empty(log);
    }

    [Fact]
    public void Persist_Fails_NotLogged()
    {
        failing = true;

        Assert.Throws<IOException>(() => Locked(scheduler.Persist));

        Assert.Empty(log);
    }

    [Fact]
    public void TimerSave_Fails_RetriedEverySaveDelay()
    {
        Locked(scheduler.PersistSoon);
        failing = true;

        time.Advance(Limits.SaveDelay);
        time.Advance(Limits.SaveDelay);

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void TimerSave_FailsRepeatedly_LoggedOnce()
    {
        Locked(scheduler.PersistSoon);
        failing = true;

        time.Advance(Limits.SaveDelay);
        time.Advance(Limits.SaveDelay);

        Assert.Equal("Serverdaten nicht gespeichert, neuer Versuch alle 2 s: Datei gesperrt", Assert.Single(log));
    }

    [Fact]
    public void TimerSave_NoAccess_LoggedLikeAFailedWrite()
    {
        Locked(scheduler.PersistSoon);
        failing = true;
        failure = new UnauthorizedAccessException("kein Zugriff");

        time.Advance(Limits.SaveDelay);
        time.Advance(Limits.SaveDelay);

        Assert.Equal("Serverdaten nicht gespeichert, neuer Versuch alle 2 s: kein Zugriff", Assert.Single(log));
    }

    [Fact]
    public void TimerSave_WorksAgainAfterAFailure_LogsSavingAgain()
    {
        Locked(scheduler.PersistSoon);
        failing = true;
        time.Advance(Limits.SaveDelay);

        failing = false;
        time.Advance(Limits.SaveDelay);

        Assert.Equal("Serverdaten werden wieder gespeichert", log[^1]);
    }

    [Fact]
    public void TimerSave_WorksAgainAfterAFailure_NotRetried()
    {
        Locked(scheduler.PersistSoon);
        failing = true;
        time.Advance(Limits.SaveDelay);

        failing = false;
        time.Advance(Limits.SaveDelay);
        time.Advance(Limits.SaveDelay);

        Assert.Equal(2, attempts);
    }

    [Fact]
    public void TimerSave_FailsAgainAfterARecovery_LoggedAgain()
    {
        Locked(scheduler.PersistSoon);
        failing = true;
        time.Advance(Limits.SaveDelay);
        failing = false;
        time.Advance(Limits.SaveDelay);

        Locked(scheduler.PersistSoon);
        failing = true;
        time.Advance(Limits.SaveDelay);

        Assert.Equal(2, log.Count(l => l.StartsWith("Serverdaten nicht gespeichert")));
    }

    [Fact]
    public void Persist_AfterAFailedTimerSave_LogsSavingAgain()
    {
        Locked(scheduler.PersistSoon);
        failing = true;
        time.Advance(Limits.SaveDelay);

        failing = false;
        Locked(scheduler.Persist);

        Assert.Equal("Serverdaten werden wieder gespeichert", log[^1]);
    }

    [Fact]
    public void FlushPending_NothingPending_WritesNothing()
    {
        Locked(scheduler.FlushPending);

        Assert.Equal(0, attempts);
    }

    [Fact]
    public void FlushPending_Pending_WritesOnce()
    {
        Locked(scheduler.PersistSoon);

        Locked(scheduler.FlushPending);
        Locked(scheduler.FlushPending);
        time.Advance(Limits.SaveDelay);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Close_NothingPendingNotForced_WritesNothing()
    {
        Locked(() => scheduler.Close(force: false));

        Assert.Equal(0, attempts);
    }

    [Fact]
    public void Close_Pending_WritesOnce()
    {
        Locked(scheduler.PersistSoon);

        Locked(() => scheduler.Close(force: false));
        time.Advance(Limits.SaveDelay);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Close_Forced_WritesAlsoWithNothingPending()
    {
        Locked(() => scheduler.Close(force: true));

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Close_SaveFails_LoggedNotThrown()
    {
        failing = true;

        Locked(() => scheduler.Close(force: true));

        Assert.Equal("Serverdaten beim Beenden nicht gespeichert: Datei gesperrt", Assert.Single(log));
    }

    [Fact]
    public void Close_NoAccess_LoggedNotThrown()
    {
        failing = true;
        failure = new UnauthorizedAccessException("kein Zugriff");

        Locked(() => scheduler.Close(force: true));

        Assert.Equal("Serverdaten beim Beenden nicht gespeichert: kein Zugriff", Assert.Single(log));
    }

    [Fact]
    public void PersistSoon_AfterClose_TimerWritesNothing()
    {
        Locked(() => scheduler.Close(force: false));

        Locked(scheduler.PersistSoon);
        time.Advance(Limits.SaveDelay);

        Assert.Equal(0, attempts);
    }

    /// <summary>The timer was due together with the close and waited for the gate: it finds the scheduler closed.</summary>
    [Fact]
    public void TimerSave_DueWhileClosing_WritesNothing()
    {
        using var closer = time.CreateTimer(_ => Locked(() => scheduler.Close(force: false)), null, Limits.SaveDelay, Timeout.InfiniteTimeSpan);
        Locked(scheduler.PersistSoon);
        failing = true; // the save on close fails, so the change stays pending

        time.Advance(Limits.SaveDelay);

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void PersistAtMostOncePerMinute_SameKeyWithinAMinute_SavedOnce()
    {
        var key = Guid.NewGuid();
        Locked(() => scheduler.PersistAtMostOncePerMinute(key));

        time.Advance(TimeSpan.FromMinutes(1) - Tick);
        Locked(() => scheduler.PersistAtMostOncePerMinute(key));

        Assert.Equal(1, saves);
    }

    [Fact]
    public void PersistAtMostOncePerMinute_SameKeyAfterAMinute_SavedAgain()
    {
        var key = Guid.NewGuid();
        Locked(() => scheduler.PersistAtMostOncePerMinute(key));

        time.Advance(TimeSpan.FromMinutes(1));
        Locked(() => scheduler.PersistAtMostOncePerMinute(key));

        Assert.Equal(2, saves);
    }

    [Fact]
    public void PersistAtMostOncePerMinute_OtherKey_SavedAtOnce()
    {
        Locked(() => scheduler.PersistAtMostOncePerMinute(Guid.NewGuid()));

        Locked(() => scheduler.PersistAtMostOncePerMinute(Guid.NewGuid()));

        Assert.Equal(2, saves);
    }
}
