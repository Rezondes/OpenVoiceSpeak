using OVS.Shared.Protocol.Messages;

namespace OVS.Tests.TestSupport;

/// <summary>Reads the requests a test recorded as sent.</summary>
internal static class SentRequests
{
    /// <summary>The request id of the last sent request of type <typeparamref name="T"/>; fails when none was sent.</summary>
    internal static string LastRequestId<T>(this IEnumerable<Request> sent) where T : Request => sent.OfType<T>().Last().RequestId!;
}
