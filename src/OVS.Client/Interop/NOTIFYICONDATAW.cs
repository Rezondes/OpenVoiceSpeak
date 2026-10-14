namespace OVS.Client.Interop;

/// <summary>Package 162: the tray icon and its balloon for <c>Shell_NotifyIconW</c>.</summary>
internal unsafe struct NOTIFYICONDATAW
{
    /// <summary>The size of this struct.</summary>
    public uint cbSize;
    /// <summary>The window that gets the icon's messages.</summary>
    public nint hWnd;
    /// <summary>The icon's id, which fields are set, and the message the shell sends for the icon.</summary>
    public uint uID, uFlags, uCallbackMessage;
    /// <summary>The icon in the tray.</summary>
    public nint hIcon;
    /// <summary>The icon's tooltip.</summary>
    public fixed char szTip[128];
    /// <summary>The icon's state and which of it is set.</summary>
    public uint dwState, dwStateMask;
    /// <summary>The balloon's text.</summary>
    public fixed char szInfo[256];
    /// <summary>The balloon's timeout (ignored since Windows Vista) or the version of the icon's behaviour.</summary>
    public uint uTimeoutOrVersion;
    /// <summary>The balloon's title.</summary>
    public fixed char szInfoTitle[64];
    /// <summary>The balloon's icon and sound.</summary>
    public uint dwInfoFlags;
    /// <summary>The icon's id as a GUID (not used).</summary>
    public Guid guidItem;
    /// <summary>A balloon icon of its own (not used).</summary>
    public nint hBalloonIcon;
}
