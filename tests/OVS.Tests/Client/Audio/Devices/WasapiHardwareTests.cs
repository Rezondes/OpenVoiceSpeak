using NAudio.CoreAudioApi;
using OVS.Client.Audio.Devices;

namespace OVS.Tests.Client.Audio.Devices;

public class WasapiHardwareTests
{
    [Theory]
    [InlineData(AudioDirection.Input, DataFlow.Capture)]
    [InlineData(AudioDirection.Output, DataFlow.Render)]
    public void ToDataFlow_EachDirection_MapsToWindowsFlow(AudioDirection direction, DataFlow expected) =>
        Assert.Equal(expected, WasapiHardware.ToDataFlow(direction));

    [Fact]
    public void OpenOutput_LibraryMissing_ThrowsItsReasonAndOpensNothing()
    {
        var hardware = new WasapiHardware("ovs-no-such-audio");

        var thrown = Assert.Throws<InvalidOperationException>(() => hardware.OpenOutput(null, []));

        Assert.Contains("ovs-no-such-audio", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenInput_LibraryMissing_ThrowsItsReason()
    {
        var hardware = new WasapiHardware("ovs-no-such-audio");

        var thrown = Assert.Throws<InvalidOperationException>(() => hardware.OpenInput(null, []));

        Assert.Contains("ovs-no-such-audio", thrown.Message, StringComparison.Ordinal);
    }
}