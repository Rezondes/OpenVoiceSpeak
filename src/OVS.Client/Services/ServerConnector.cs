using Microsoft.Extensions.Logging;
using OVS.Client.Dialogs;
using OVS.Client.Logging;
using OVS.Client.Net;

namespace OVS.Client.Services;

/// <summary>
/// Step 30d5: the real connect, made by the composition root: the profile's identity, the trusted fingerprints and the
/// certificate question, whose answer is logged.
/// </summary>
/// <param name="profileDir">The profile folder with the identity file.</param>
/// <param name="known">The trusted server certificates.</param>
/// <param name="confirmTofu">Asks the user about an unknown or changed server certificate.</param>
/// <param name="log">The client's log.</param>
/// <param name="time">The client's one clock.</param>
public sealed class ServerConnector(string profileDir, TrustedServerFingerprints known, Func<TofuPrompt, Task<bool>> confirmTofu, ILogger log,
    TimeProvider time)
{
    /// <summary>Opens a connection as <paramref name="choice"/> says; fits <see cref="OpenConnection"/>.</summary>
    public async Task<ClientConnection> OpenAsync(ConnectChoice choice, CancellationToken token)
    {
        using var identity = IdentityFile.LoadOrCreate(profileDir);
        return await ClientConnection.ConnectAsync(choice.Host, choice.Port, identity, choice.Nickname,
            choice.PasswordOrNull, known, ConfirmLoggedAsync, time, token);
    }

    async Task<bool> ConfirmLoggedAsync(TofuPrompt prompt)
    {
        bool accepted = await confirmTofu(prompt);
        log.ServerCertificate(prompt.Result == TofuResult.Mismatch ? "CHANGED" : "new", prompt.Host, prompt.Port, prompt.Fingerprint, accepted ? "accepted" : "refused");
        return accepted;
    }
}
