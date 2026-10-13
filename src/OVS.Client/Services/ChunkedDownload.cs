using OVS.Shared.IO;

namespace OVS.Client.Services;

/// <summary>Packages 75 and 82: pulls a file chunk by chunk into target.part and moves it into place only after the last chunk,
/// so a failed transfer leaves no half file and the file there before untouched.</summary>
static class ChunkedDownload
{
    /// <param name="next">Asks for the chunk at an offset; null stops (error, timeout, page closed).</param>
    /// <returns>True once the file is in place. IO and base64 errors throw, after removing the temporary file.</returns>
    /// <param name="target">The file to write.</param>
    /// <param name="progress">Gets the bytes loaded and the total after each chunk.</param>
    public static async Task<bool> RunAsync(string target, Func<long, Task<DownloadChunk?>> next, Action<long, long> progress)
    {
        var temp = target + ".part";
        bool done = false;
        try
        {
            await using (var file = File.Create(temp))
            {
                while (true)
                {
                    if (await next(file.Length) is not { } chunk || chunk.Offset != file.Length) return false;
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await file.WriteAsync(bytes);
                    progress(file.Length, chunk.Total);
                    if (chunk.IsLast) break;
                    if (bytes.Length == 0) return false;
                }
            }
            File.Move(temp, target, overwrite: true);
            done = true;
            return true;
        }
        finally
        {
            if (!done) SafeFile.DeleteQuietly(temp);
        }
    }
}
