using CommunityToolkit.Mvvm.ComponentModel;
using OVS.Client.Localization;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.ServerBackups;

namespace OVS.Client.Services;

/// <summary>
/// Packages 75 (A91) and 82 (A99): the administration's file transfers to and from this PC, chunk by chunk and one at a
/// time: a backup down or up, the chosen log files down. The backups and the logs tab share it, and with it the progress.
/// </summary>
/// <param name="send">Sends a request whose answers, and the server's error for it, go to the given route.</param>
/// <param name="removeRoutes">Removes the routes to a handler, so no further answer reaches it.</param>
/// <param name="showNotice">Shows a failed transfer to the user.</param>
/// <param name="time">The clock an answer is waited for on.</param>
internal sealed partial class AdminTransfer(Func<Request, Action<Message>, Task> send, Action<Action<Message>> removeRoutes, Action<string> showNotice,
    TimeProvider time) : ObservableObject
{
    static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);
    [ObservableProperty] bool isRunning;
    [ObservableProperty] double percent;
    [ObservableProperty] string progressText = "";
    /// <summary>The answer awaited now; one request at a time.</summary>
    TaskCompletionSource<Message>? reply;
    /// <summary>
    /// Where the answers to the latest request go. It stays until the next request or <see cref="Cancel"/>, since the server
    /// can send more after the answer (the new backup list after an upload's last chunk).
    /// </summary>
    Action<Message>? route;

    /// <summary>Package 75: the administration page closed; a running transfer stops and cleans up, and no answer reaches it any more.</summary>
    internal void Cancel()
    {
        reply?.TrySetCanceled();
        RemoveRoute();
    }

    void RemoveRoute()
    {
        if (route is not null) removeRoutes(route);
        route = null;
    }

    /// <summary>The server's answer to one request, or null (error, timeout, page closed). The server shows its own errors as a notice.</summary>
    /// <param name="failedText">The notice when no answer comes in time.</param>
    /// <param name="followUp">Gets what the server sends after the answer.</param>
    /// <param name="request">The request to send.</param>
    async Task<Message?> RequestAsync(Request request, string failedText, Action<Message>? followUp = null)
    {
        RemoveRoute();
        var awaited = reply = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        route = answer =>
        {
            if (!awaited.TrySetResult(answer)) followUp?.Invoke(answer);
        };
        await send(request, route);
        try
        {
            var answer = await awaited.Task.WaitAsync(AnswerTimeout, time);
            return answer is Error ? null : answer;
        }
        catch (TimeoutException)
        {
            showNotice(failedText);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    void ShowProgress(string format, long done, long total)
    {
        Percent = total <= 0 ? 100 : done * 100d / total;
        ProgressText = string.Format(format, (int)Math.Round(Percent));
    }

    /// <summary>
    /// One transfer, none while another runs: the progress starts at zero; an IO error, and with catchFormat a chunk that is
    /// not base64, ends it with the notice failedText.
    /// </summary>
    async Task RunAsync(string progressFormat, string failedText, bool catchFormat, Func<Task> body)
    {
        if (IsRunning) return;
        IsRunning = true;
        ShowProgress(progressFormat, 0, 1);
        try
        {
            await body();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException || (catchFormat && exception is FormatException))
        {
            showNotice(failedText);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Writes to a temporary file next to target and moves it into place only once the last chunk arrived.</summary>
    internal Task DownloadBackupAsync(string fileName, string target) =>
        RunAsync(Strings.Backup_Downloading, Strings.Backup_TransferFailed, catchFormat: true, () => ChunkedDownload.RunAsync(target,
            async offset => await RequestAsync(new DownloadBackup(fileName, offset), Strings.Backup_TransferFailed) is BackupChunk backupChunk
                ? new(backupChunk.Offset, backupChunk.TotalSize, backupChunk.DataBase64, backupChunk.IsLast) : null,
            (done, total) => ShowProgress(Strings.Backup_Downloading, done, total)));

    /// <summary>
    /// Package 82 (A99): the server copies the chosen log files (one .log or a zip), then they come chunk by chunk like a backup,
    /// with the same progress.
    /// </summary>
    internal Task DownloadLogsAsync(IReadOnlyList<string> fileIds, string target) => fileIds.Count == 0 ? Task.CompletedTask
        : RunAsync(Strings.Logs_Downloading, Strings.Logs_TransferFailed, catchFormat: true, async () =>
        {
            if (await RequestAsync(new PrepareLogDownload(fileIds), Strings.Logs_TransferFailed) is not LogDownloadReady ready) return;
            await ChunkedDownload.RunAsync(target,
                async offset => await RequestAsync(new DownloadLogChunk(ready.DownloadId, offset), Strings.Logs_TransferFailed) is LogChunk logChunk
                    ? new(logChunk.Offset, logChunk.TotalSize, logChunk.DataBase64, logChunk.IsLast) : null,
                (done, total) => ShowProgress(Strings.Logs_Downloading, done, total));
        });

    /// <summary>Sends the file in chunks, each after the server's answer.</summary>
    /// <param name="followUp">Gets the backup list the server sends after storing the upload.</param>
    /// <returns>The backup the server stored; null when another transfer runs, the file is too large, the server refused it or the transfer failed.</returns>
    /// <param name="path">The archive to upload.</param>
    internal async Task<BackupInfo?> UploadBackupAsync(string path, Action<Message> followUp)
    {
        BackupInfo? uploaded = null;
        await RunAsync(Strings.Backup_Uploading, Strings.Backup_TransferFailed, catchFormat: false, async () =>
        {
            await using var file = File.OpenRead(path);
            if (file.Length > ProtocolLimits.MaxBackupUploadBytes)
            {
                showNotice(ErrorTexts.For(ReasonCodes.BackupTooLarge));
                return;
            }
            var uploadId = Guid.NewGuid().ToString("N");
            var buffer = new byte[ProtocolLimits.BackupChunkBytes];
            for (long offset = 0; ;)
            {
                int read = await file.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);
                bool last = offset + read >= file.Length;
                var chunk = new UploadBackupChunk(uploadId, offset, Convert.ToBase64String(buffer, 0, read), last);
                var answer = await RequestAsync(chunk, Strings.Backup_TransferFailed, followUp);
                offset += read;
                ShowProgress(Strings.Backup_Uploading, offset, file.Length);
                if (answer is BackupUploaded backupUploaded) uploaded = backupUploaded.Backup;
                if (last || answer is not UploadBackupAck) break;
            }
        });
        return uploaded;
    }
}
