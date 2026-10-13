namespace OVS.Tests.TestSupport;

/// <summary>
/// The test classes that show a <c>MainWindow</c> or start animations run one after another: the counters of
/// <c>AnimationCounter</c> are static, and a test of another collection that runs in parallel (or whose queued work runs
/// inside a walk's frames) would count its animations mid-test. The display is no reason any more: each window has its
/// own (<c>Motion.DisplayProperty</c>).
/// </summary>
[CollectionDefinition(Name)]
#pragma warning disable CA1515 // public: xunit finds collection definitions on public classes
#pragma warning disable CA1711 // an xunit collection definition, named after its collection
public sealed class MotionCollection
#pragma warning restore CA1711
#pragma warning restore CA1515
{
    public const string Name = "Motion";
}
