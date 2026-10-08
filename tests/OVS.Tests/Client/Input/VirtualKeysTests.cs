using OVS.Client.Input;

namespace OVS.Tests.Client.Input;

/// <summary>Which virtual keys are modifiers, and how a key is shown.</summary>
public class VirtualKeysTests
{
    [Theory]
    [InlineData(VirtualKeys.Shift, true)]
    [InlineData(VirtualKeys.Control, true)]
    [InlineData(VirtualKeys.Menu, true)]
    [InlineData(0xA0, true)]
    [InlineData(VirtualKeys.RControl, true)]
    [InlineData(0xA5, true)]
    [InlineData(0x9F, false)]
    [InlineData(0xA6, false)]
    [InlineData(VirtualKeys.XButton1, false)]
    [InlineData(0x70, false)]
    public void IsModifier_EachKey_ShiftCtrlAltAndTheirSidesOnly(int vk, bool modifier) => Assert.Equal(modifier, VirtualKeys.IsModifier(vk));

    [Theory]
    [InlineData(VirtualKeys.Shift, false)]
    [InlineData(VirtualKeys.Control, false)]
    [InlineData(VirtualKeys.Menu, false)]
    [InlineData(0xA0, true)]
    [InlineData(VirtualKeys.RControl, true)]
    [InlineData(0xA5, true)]
    [InlineData(0x9F, false)]
    [InlineData(0xA6, false)]
    [InlineData(VirtualKeys.XButton1, false)]
    [InlineData(0x70, false)]
    public void IsSidedModifier_EachKey_LeftAndRightVariantsOnly(int vk, bool sided) => Assert.Equal(sided, VirtualKeys.IsSidedModifier(vk));

    [Theory]
    [InlineData(0, "(keine)")]
    [InlineData(VirtualKeys.XButton1, "Maustaste 4")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x4D, "M")]
    [InlineData(0x30, "0")]
    [InlineData(VirtualKeys.Shift, "Umschalt")]
    [InlineData(VirtualKeys.Control, "Strg")]
    [InlineData(VirtualKeys.Menu, "Alt")]
    [InlineData(VirtualKeys.RControl, "Strg rechts")]
    public void Localize_KnownKey_ItsGermanName(int vk, string name) => Assert.Equal(name, VirtualKeys.Localize(vk));

    [Fact]
    public void Localize_UnknownKey_NamesItsCode() => Assert.Equal("Taste 0xE0", VirtualKeys.Localize(0xE0));
}
