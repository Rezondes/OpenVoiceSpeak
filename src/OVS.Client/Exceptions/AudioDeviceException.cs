namespace OVS.Client.Exceptions;

/// <summary>Package 173: an audio device of the native library could not open or stopped; <see cref="Exception.HResult"/> holds its HRESULT.</summary>
internal sealed class AudioDeviceException : Exception
{
    /// <summary>An audio device failed.</summary>
    public AudioDeviceException() : base("An audio device failed.")
    {
    }

    /// <summary>With the text <paramref name="message"/>.</summary>
    public AudioDeviceException(string message) : base(message)
    {
    }

    /// <summary>With the text <paramref name="message"/>, caused by <paramref name="innerException"/>.</summary>
    public AudioDeviceException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>With the text <paramref name="message"/> and the device's <paramref name="hresult"/>.</summary>
    public AudioDeviceException(string message, int hresult) : base(message) => HResult = hresult;
}
