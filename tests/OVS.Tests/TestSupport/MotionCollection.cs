namespace OVS.Tests.TestSupport;

/// <summary>
/// The test classes that show a <c>MainWindow</c> or start animations run one after another: the counters of
/// <c>AnimationCounter</c> are static, and a test of another collection that runs in parallel (or whose queued work runs
/// inside a walk's frames) would count its animations mid-test. The display is no reason any more: each window has its
/// own (<c>Motion.DisplayProperty</c>).
/// </summary>
[CollectionDefinition(Name)]
public sealed class MotionCollection
{
    public const string Name = "Motion";
}
