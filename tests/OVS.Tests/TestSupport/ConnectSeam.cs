using System.Net.Sockets;
using OVS.Client.Dialogs;
using OVS.Client.Net;
using OVS.Client.Services;

namespace OVS.Tests.TestSupport;

/// <summary>
/// The connect seam of the view model for a test: records every call as a <see cref="ConnectAttempt"/> and answers it as the
/// test scripted (the next call fails, waits for a release, or every call fails); a call nothing is scripted for connects for real
/// with the profile's identity. Hand <see cref="Connect"/> to <see cref="MainViewModelSetup"/>.
/// Behind the seam the view model's own trusted fingerprints, its <c>ConfirmTofu</c> hook and the "Serverzertifikat" log line are
/// skipped: the real connect trusts every certificate, and a refused certificate is scripted with <see cref="TofuRejectedException"/>.
/// </summary>
sealed class ConnectSeam(string profileDir, TimeProvider time)
{
    readonly object gate = new();
    readonly List<ConnectAttempt> attempts = [];
    readonly Dictionary<int, TaskCompletionSource<ConnectAttempt>> waiters = [];
    readonly Queue<Func<ConnectAttempt, Task<ClientConnection>>> scripted = [];
    Func<ConnectAttempt, Task<ClientConnection>>? fallback;

    public OpenConnection Connect => ConnectAsync;

    /// <summary>Every call so far, oldest first.</summary>
    public IReadOnlyList<ConnectAttempt> Attempts
    {
        get
        {
            lock (gate) return [.. attempts];
        }
    }

    /// <summary>Queues a failure for the next call after those already scripted.</summary>
    public void FailNext(Exception failure) => Script(_ => Task.FromException<ClientConnection>(failure));

    /// <summary>Every call from now on that is not scripted throws what <paramref name="failure"/> makes (a connect refused at once).</summary>
    public void FailEvery(Func<Exception> failure)
    {
        lock (gate) fallback = _ => Task.FromException<ClientConnection>(Make(failure));
    }

    /// <summary>
    /// The next call after those already scripted waits (it honours its token) until the returned <see cref="HeldAttempt"/> is
    /// released; then it throws <paramref name="failWith"/> when given and otherwise connects for real.
    /// </summary>
    public HeldAttempt HoldNext(Exception? failWith = null)
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            int number = attempts.Count + scripted.Count + 1;
            scripted.Enqueue(async attempt =>
            {
                await released.Task.WaitAsync(attempt.Token);
                return failWith is null ? await ConnectForRealAsync(attempt) : throw failWith;
            });
            return new HeldAttempt(() => released.TrySetResult(), GetOrAddWaiter(number).Task);
        }
    }

    /// <summary>Completes with the call of that number (from 1) once it was made; wrap it in <see cref="HangGuard.Bound{T}"/>.</summary>
    public Task<ConnectAttempt> WaitForAttemptAsync(int number)
    {
        lock (gate) return GetOrAddWaiter(number).Task;
    }

    /// <summary>The error the system reports at once for a connect to a port nobody listens on.</summary>
    public static SocketException CreateClosedPortError() => new((int)SocketError.ConnectionRefused);

    static Exception Make(Func<Exception> failure)
    {
        try
        {
            return failure();
        }
        catch (Exception e)
        {
            return e;
        }
    }

    void Script(Func<ConnectAttempt, Task<ClientConnection>> behaviour)
    {
        lock (gate) scripted.Enqueue(behaviour);
    }

    TaskCompletionSource<ConnectAttempt> GetOrAddWaiter(int number)
    {
        if (!waiters.TryGetValue(number, out var waiter))
            waiters[number] = waiter = new TaskCompletionSource<ConnectAttempt>(TaskCreationOptions.RunContinuationsAsynchronously);
        return waiter;
    }

    Task<ClientConnection> ConnectAsync(ConnectChoice choice, CancellationToken token)
    {
        ConnectAttempt attempt;
        Func<ConnectAttempt, Task<ClientConnection>> behaviour;
        lock (gate)
        {
            attempt = new ConnectAttempt(attempts.Count + 1, choice, token);
            attempts.Add(attempt);
            behaviour = scripted.Count > 0 ? scripted.Dequeue() : fallback ?? ConnectForRealAsync;
            GetOrAddWaiter(attempt.Number).TrySetResult(attempt);
        }
        return behaviour(attempt);
    }

    async Task<ClientConnection> ConnectForRealAsync(ConnectAttempt attempt)
    {
        using var identity = IdentityFile.LoadOrCreate(profileDir);
        return await ClientConnection.ConnectAsync(attempt.Choice.Host, attempt.Choice.Port, identity, attempt.Choice.Nickname,
            attempt.Choice.PasswordOrNull, new TrustedServerFingerprints(profileDir), _ => Task.FromResult(true), time, attempt.Token);
    }
}
