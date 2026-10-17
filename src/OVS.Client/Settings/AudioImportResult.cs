using OVS.Client.Models;

namespace OVS.Client.Settings;

/// <summary>Package 171: what <see cref="ChatAppAudioImport.Read"/> found, or why it found nothing (for the client log).</summary>
/// <param name="Audio">The values found; null when nothing could be read.</param>
/// <param name="Problem">Why nothing was read, in English; null when something was.</param>
internal sealed record AudioImportResult(ImportedAudio? Audio, string? Problem);
