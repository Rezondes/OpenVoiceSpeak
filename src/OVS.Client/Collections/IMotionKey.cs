namespace OVS.Client.Collections;

/// <summary>
/// Package 101: lists that are rebuilt with new objects (the administration, the bookmarks) name what stays the same,
/// so only really new entries play the appear animation. Items without it count by their identity.
/// </summary>
public interface IMotionKey
{
    /// <summary>The same for the same entry when the list is rebuilt, so its motion continues.</summary>
    object MotionKey { get; }

    /// <summary>The key an item counts by: its <see cref="MotionKey"/>, or the item itself.</summary>
    static object? KeyOf(object? item) => item is IMotionKey keyed ? keyed.MotionKey : item;
}
