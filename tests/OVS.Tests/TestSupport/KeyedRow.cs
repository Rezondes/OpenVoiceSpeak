using OVS.Client.Collections;

namespace OVS.Tests.TestSupport;

/// <summary>An entry of a rebuilt list: new objects, the same key.</summary>
sealed record KeyedRow(string Key, int Version = 0) : IMotionKey
{
    public object MotionKey => Key;
}
