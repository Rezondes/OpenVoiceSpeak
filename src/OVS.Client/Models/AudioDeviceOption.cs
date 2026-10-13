namespace OVS.Client.Models;

/// <summary>An entry of a device list: the device's id (null for the system default) and the name shown.</summary>
public sealed record AudioDeviceOption(string? Id, string Name);
