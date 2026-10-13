namespace OVS.Client.Models;

/// <summary>An entry of a device list: the device's id (null for the system default) and the name shown.</summary>
internal sealed record AudioDeviceOption(string? Id, string Name);
