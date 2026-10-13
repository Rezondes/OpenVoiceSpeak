namespace OVS.Client.Dialogs;

/// <summary>The channel dialog only offers the channel options when editing (Package 34).</summary>
public enum ChannelDialogMode
{
    /// <summary>A new channel.</summary>
    Create,
    /// <summary>An existing channel.</summary>
    Edit,
    /// <summary>The default channel: no group lock, no password, no slots.</summary>
    EditDefault,
}
