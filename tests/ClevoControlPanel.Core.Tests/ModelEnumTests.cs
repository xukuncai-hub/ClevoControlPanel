using System;
using ClevoControlPanel.Core.Models;
using FluentAssertions;
using Xunit;

namespace ClevoControlPanel.Core.Tests;

public class ModelEnumTests
{
    [Fact]
    public void PowerMode_Values_MatchClevoEcSpecification()
    {
        ((byte)PowerMode.Quiet).Should().Be(0);
        ((byte)PowerMode.PowerSaving).Should().Be(1);
        ((byte)PowerMode.Performance).Should().Be(2);
        ((byte)PowerMode.Entertainment).Should().Be(3);

        Enum.GetValues<PowerMode>().Should().HaveCount(4);
    }

    [Fact]
    public void FanMode_Values_MatchClevoEcSpecification()
    {
        ((byte)FanMode.Auto).Should().Be(0);
        ((byte)FanMode.Maximum).Should().Be(1);
        ((byte)FanMode.Silent).Should().Be(2);
        ((byte)FanMode.MaxQ).Should().Be(5);
        ((byte)FanMode.Custom).Should().Be(6);
        ((byte)FanMode.Noiseless).Should().Be(8);

        Enum.GetValues<FanMode>().Should().HaveCount(6);
    }

    [Theory]
    [InlineData(0, PowerMode.Quiet)]
    [InlineData(1, PowerMode.PowerSaving)]
    [InlineData(2, PowerMode.Performance)]
    [InlineData(3, PowerMode.Entertainment)]
    public void PowerMode_CanBeCastFromByte(byte rawByte, PowerMode expectedMode)
    {
        ((PowerMode)rawByte).Should().Be(expectedMode);
    }

    [Theory]
    [InlineData(0, FanMode.Auto)]
    [InlineData(1, FanMode.Maximum)]
    [InlineData(2, FanMode.Silent)]
    [InlineData(5, FanMode.MaxQ)]
    [InlineData(6, FanMode.Custom)]
    [InlineData(8, FanMode.Noiseless)]
    public void FanMode_CanBeCastFromByte(byte rawByte, FanMode expectedMode)
    {
        ((FanMode)rawByte).Should().Be(expectedMode);
    }
}
