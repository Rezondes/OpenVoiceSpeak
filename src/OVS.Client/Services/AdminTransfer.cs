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
/// <param name="send">Sends a request and returns the id it got.</param>
/// <param name="showNotice">Shows a failed transfer to the user.</param>
/// <param name="time">The clock an answer is waited for on.</param>
internal sealed partial class AdminTransfer(Func<Request, Task<string>> send, Action<string> showNotice, TimeProvider time) : ObservableObject
{
    static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);
    [ObservableProperty] bool isRunning;
    [ObservableProperty] double percent;
    [ObservableProperty] string progressText = "";
    /// <summary>Answers by request id; an answer can come before SendAsync returns, so whoever comes first adds the entry.</summary>
    readonly Dictionary<string, TaskCompletionSource<Message>> replies = [];

    /// <summary>The server's answers, passed on by the administration page; an error counts only while a transfer runs.</summary>
    internal void Apply(Message message)
    {
        switch (message)
        {
            case Error e:
                if (IsRunning && e.RequestId is { } failed) ReplyFor(failed).TrySetResult(e);
                break;
            case BackupChunk { RequestId: { } id }:
                ReplyFor(id).TrySetResult(message);
                break;
            case UploadBackupAck { RequestId: { } id }:
                ReplyFor(id).TrySetResult(message);
                break;
            case BackupUploaded { RequestId: { } id }:
                ReplyFor(id).TrySetResult(message);
                break;
            case LogDownloadReady { RequestId: { } id }: // Package 82
                ReplyFor(id).TrySetResult(message);
                break;
            case LogChunk { RequestId: { } id }:
                ReplyFor(id).TrySetResult(message);
                break;
        }
    }

    /// <summary>Package 75: the administration page closed; a running transfer stops and cleans up.</summary>
    internal void Cancel()
    {
        foreach (var reply in replies.Values) reply.TrySetCanceled();
    }

    TaskCompletionSource<Message> ReplyFor(string requestId)
    {
        if (!replies.TryGetValue(requestId, out var reply))
            replies[requestId] = reply = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        return reply;
    }

    /// <summary>The server's answer to one request, or null (error, timeout, page closed). The server shows its own errors as a notice.</summary>
    /// <param name="failedText">The notice when no answer comes in time.</param>
    async Task<Message?> RequestAsync(Request request, string failedText)
    {
        var id = await send(request);
        try
        {
            var answer = await ReplyFor(id).Task.WaitAsync(AnswerTimeout, time);
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
        finally
        {
            replies.Remove(id);
        }
    }

    void ShowProgress(string format, long done, long total)
    {
        Percent = total <= 0 ? 100 : done * 100d / total;
        ProgressText = string.Format(format, (int)Math.Round(Percent));
    }

    /// <summary>
    /// One transfer, none while another runs: the progress starts at zero; an IO error, and with catchFormat a chunk that is
    /// not base64, ends it with the notice failedText; at the end the answers left over go.
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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException || (catchFormat && e is FormatException))
        {
            showNotice(failedText);
        }
        finally
        {
            replies.Clear(); // answers to other failed requests that came in meanwhile
            IsRunning = false;
        }
    }

    /// <summary>Writes to a temporary file next to target and moves it into place only once the last chunk arrived.</summary>
    internal Task DownloadBackupAsync(string fileName, string target) =>
        RunAsync(Strings.Backup_Downloading, Strings.Backup_TransferFailed, catchFormat: true, () => ChunkedDownload.RunAsync(target,
            async offset => await RequestAsync(new DownloadBackup(fileName, offset), Strings.Backup_TransferFailed) is BackupChunk c
                ? new(c.Offset, c.TotalSize, c.DataBase64, c.IsLast) : null,
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
                async offset => await RequestAsync(new DownloadLogChunk(ready.DownloadId, offset), Strings.Logs_TransferFailed) is LogChunk c
                    ? new(c.Offset, c.TotalSize, c.DataBase64, c.IsLast) : null,
                (done, total) => ShowProgress(Strings.Logs_Downloading, done, total));
        });

    /// <summary>Sends the file in chunks, each after the server's answer.</summary>
    /// <returns>The backup the server stored; null when another transfer runs, the file is too large, the server refused it or the transfer failed.</returns>
    internal async Task<BackupInfo?> UploadBackupAsync(string path)
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
                var answer = await RequestAsync(chunk, Strings.Backup_TransferFailed);
                offset += read;
                ShowProgress(Strings.Backup_Uploading, offset, file.Length);
                if (answer is BackupUploaded u) uploaded = u.Backup;
                if (last || answer is not UploadBackupAck) break;
            }
        });
        return uploaded;
    }
}
