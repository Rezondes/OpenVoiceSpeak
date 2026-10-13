namespace OVS.Tests.TestSupport;

/// <summary>One export an <see cref="OtlpReceiver"/> got: the path it was posted to and its protobuf body.</summary>
internal sealed record OtlpExport(string Path, byte[] Body);
