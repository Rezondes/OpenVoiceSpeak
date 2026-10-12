namespace OVS.Client.Models;

/// <summary>The entries of a device combo box on the settings page and the one to select.</summary>
/// <param name="Options">The default device first, then the system's devices (or a placeholder while they load).</param>
/// <param name="Selected">The saved device, the placeholder, or the default device when the saved one is gone.</param>
/// <param name="FellBack">True when the saved device is gone, so the page shows a hint.</param>
public sealed record DeviceOptionList(IReadOnlyList<AudioDeviceOption> Options, AudioDeviceOption Selected, bool FellBack);
