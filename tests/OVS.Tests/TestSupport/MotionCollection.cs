namespace OVS.Tests.TestSupport;

/// <summary>
/// The test classes that show a <c>MainWindow</c> or switch the display (<c>Motion.Apply</c>, <c>Motion.IsAnimated</c>)
/// run one after another: the display of <c>Motion</c> and the counters of <c>AnimationCounter</c> are static, and a
/// test of another collection that runs in parallel (or whose queued work runs inside a walk's frames) would switch them
/// mid-test.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MotionCollection
{
    public const string Name = "Motion";
}
