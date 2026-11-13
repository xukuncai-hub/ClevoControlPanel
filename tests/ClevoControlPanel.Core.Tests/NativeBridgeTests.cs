using System;
using ClevoControlPanel.Core.Native;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class NativeBridgeTests
{
    [Fact]
    public void IsHardwareSupported_ExecutesWithoutThrowingExceptions()
    {
        // Safe interrogation should return either true (on Clevo host) or false (in CI/mock),
        // but must NEVER throw an unhandled exception.
        var act = () => InsydeDchuNative.IsHardwareSupported();
        act.Should().NotThrow();

        bool isSupported = InsydeDchuNative.IsHardwareSupported();
        // Caching verification: subsequent calls return identical cached value
        InsydeDchuNative.IsHardwareSupported().Should().Be(isSupported);

        // Reset cache check
        InsydeDchuNative.ResetHardwareSupportedCache();
        InsydeDchuNative.IsHardwareSupported().Should().Be(isSupported);
    }

    [Theory]
    [InlineData(121, 25, 0u)]
    [InlineData(121, 1, 0u)]
    [InlineData(121, 14, 128u)]
    public void SetWmiSubCommand_EncodesPayloadSafely(int command, int subCommand, uint data)
    {
        // On non-Clevo test machines or without admin elevation, P/Invoke might return 0 or throw DllNotFound.
        // If hardware is supported, it should return an integer status without crashing the process.
        if (InsydeDchuNative.IsHardwareSupported())
        {
            var act = () => InsydeDchuNative.SetWmiSubCommand(command, subCommand, data);
            act.Should().NotThrow();
        }
    }
}
