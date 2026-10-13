using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>A started test server with a client view model connected to it on its own UI thread.</summary>
/// <param name="Server">The running server.</param>
/// <param name="Ui">The dispatcher the view model runs on.</param>
/// <param name="ViewModel">The connected client.</param>
public sealed record ClientRig(TestServer Server, TestDispatcher Ui, MainViewModel ViewModel);
