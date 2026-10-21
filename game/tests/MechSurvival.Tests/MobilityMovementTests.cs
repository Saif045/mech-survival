using MechSurvival.Player;
using Xunit;

namespace MechSurvival.Tests;

public class MobilityMovementTests
{
    [Theory]
    [InlineData(2, true, 6)]
    [InlineData(1.5f, true, 4.5f)]
    [InlineData(1, true, 3.5f)]
    [InlineData(0.5f, true, 3.5f)]
    [InlineData(0, true, 3.5f)]
    [InlineData(2, false, 3.5f)]
    [InlineData(0.5f, false, 3.5f)]
    [InlineData(0, false, 3.5f)]
    public void CanonicalMobilityAndCapabilityDetermineSpeed(float mobility, bool capability, float speed) =>
        Assert.Equal(speed, MobilityMovementPolicy.Speed(mobility, capability));

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.MaxValue)]
    public void InvalidOrOverflowingMobilityRejectsEvenWithoutCapability(float mobility)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MobilityMovementPolicy.Speed(mobility, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => MobilityMovementPolicy.Speed(mobility, false));
    }
}
