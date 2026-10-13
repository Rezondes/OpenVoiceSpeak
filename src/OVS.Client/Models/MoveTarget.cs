using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>Package 120 (A129): one channel in a user's "Verschieben" submenu; the command moves the user there.</summary>
internal sealed record MoveTarget(ChannelViewModel Channel, System.Windows.Input.ICommand Command);
