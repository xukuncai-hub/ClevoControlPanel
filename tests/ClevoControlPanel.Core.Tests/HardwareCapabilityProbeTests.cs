using ClevoControlPanel.Core.Services;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class HardwareCapabilityProbeTests
{
    [Fact]
    public void Detect_WithoutCapabilityData_KeepsPermissiveDefaults()
    {
        var fake = FakeDchuNative.CreateInitialized();

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.SupportsRgbKeyboard.Should().BeTrue();
        capabilities.HasKeyboardBacklight.Should().BeTrue();
        capabilities.IsFanless.Should().BeFalse();
        capabilities.SupportsFanControl.Should().BeTrue();
        capabilities.SupportsFanOffset.Should().BeTrue();
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)6)]
    [InlineData((byte)22)]
    public void Detect_WhiteKeyboard_DisablesRgb(byte keyboardType)
    {
        var fake = WithKeyboardType(keyboardType);

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.SupportsRgbKeyboard.Should().BeFalse();
        capabilities.HasKeyboardBacklight.Should().BeTrue();
        capabilities.SupportsPerKeyRgb.Should().BeFalse();
    }

    [Fact]
    public void Detect_NoBacklightKeyboard_DisablesBacklight()
    {
        var fake = WithKeyboardType(0);

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.HasKeyboardBacklight.Should().BeFalse();
        capabilities.SupportsRgbKeyboard.Should().BeFalse();
    }

    [Fact]
    public void Detect_PerKeyKeyboard_ReportsPerKeyRgb()
    {
        var fake = WithKeyboardType(3);

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.SupportsRgbKeyboard.Should().BeTrue();
        capabilities.SupportsPerKeyRgb.Should().BeTrue();
    }

    [Fact]
    public void Detect_UnknownRgbKeyboardType_KeepsRgbEnabled()
    {
        var fake = WithKeyboardType(38);

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.SupportsRgbKeyboard.Should().BeTrue();
        capabilities.SupportsPerKeyRgb.Should().BeFalse();
    }

    [Fact]
    public void Detect_FanlessMachine_DisablesFanControls()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.WmiValues[122] = 1 << 15;

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.IsFanless.Should().BeTrue();
        capabilities.SupportsFanControl.Should().BeFalse();
        capabilities.SupportsFanOffset.Should().BeFalse();
    }

    [Fact]
    public void Detect_FanOffsetUnsupported_DisablesOffsetOnly()
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.WmiValues[96] = 1 << 10;

        var capabilities = HardwareCapabilityProbe.Detect(fake);

        capabilities.SupportsFanOffset.Should().BeFalse();
        capabilities.SupportsFanControl.Should().BeTrue();
    }

    private static FakeDchuNative WithKeyboardType(byte keyboardType)
    {
        var fake = FakeDchuNative.CreateInitialized();
        fake.WmiValues[70] = 256;

        var buffer = new byte[256];
        buffer[15] = keyboardType;
        fake.WmiBuffers[13] = buffer;
        return fake;
    }
}
