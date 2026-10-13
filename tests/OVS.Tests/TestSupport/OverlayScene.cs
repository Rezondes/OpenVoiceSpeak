using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Settings;

namespace OVS.Tests.TestSupport;

sealed record OverlayScene(Window Window, OverlayHost Overlay);
