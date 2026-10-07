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
}
