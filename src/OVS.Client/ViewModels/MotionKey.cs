namespace OVS.Client.ViewModels;

/// <summary>
/// Package 101: lists that are rebuilt with new objects (the administration, the bookmarks) name what stays the same,
/// so only really new entries play the appear animation. Items without it count by their identity.
/// </summary>
public interface IMotionKey
{
    object MotionKey { get; }
}
