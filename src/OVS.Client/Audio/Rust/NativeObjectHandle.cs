using System.Runtime.InteropServices;

namespace OVS.Client.Audio.Rust;

/// <summary>
/// Package 175: an object of the native library (a jitter buffer, a decoder), freed through its own free export when
/// the managed owner goes, also without a Dispose (the finalizer of the safe handle).
/// </summary>
internal sealed unsafe class NativeObjectHandle : SafeHandle
{
    readonly delegate* unmanaged[Cdecl]<nint, void> free;

    NativeObjectHandle(nint created, delegate* unmanaged[Cdecl]<nint, void> free) : base(0, ownsHandle: true)
    {
        this.free = free;
        SetHandle(created);
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == 0;

    /// <summary>Creates the object; throws when the library is unusable (A180).</summary>
    public static NativeObjectHandle Create(NativeAudio native, delegate* unmanaged[Cdecl]<nint> create, delegate* unmanaged[Cdecl]<nint, void> free)
    {
        if (native.Error is { } unusable) throw new InvalidOperationException(unusable);
        return new NativeObjectHandle(create(), free);
    }

    /// <summary>The native pointer; valid while this handle lives.</summary>
    public nint Pointer => handle;

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        free(handle);
        return true;
    }
}
