using MechSurvival.Mech.Gas;
using Xunit;

namespace MechSurvival.Tests;

public class ParasiteResourceTests
{
    [Fact] public void ExternalEnergyDrainAddsToBaseCostBeforeAffordableCooling()
    {
        var result = EnergyHeatPolicy.Step(new(10, 20), 10, 10, 0, 1, new(2, 1, 1), 3);
        Assert.Equal(2, result.BaseEnergySpent); Assert.Equal(3, result.ExternalEnergySpent);
        Assert.Equal(5, result.CoolingEnergySpent); Assert.Equal(new EnergyHeatState(0, 15), result.State);
    }
    [Fact] public void ParasiteHeatComposesWithExternalHeatBeforeAcceptedCooling()
    {
        var result = EnergyHeatPolicy.Step(new(10, 10), 10, 5, 1, 1, EnergyHeatSettings.Default, 0.2f, 6);
        Assert.InRange(result.State.Energy, 8.44999f, 8.45001f); Assert.Equal(12, result.State.Heat);
        Assert.Equal(7, result.HeatAdded); Assert.Equal(5, result.HeatRemoved);
    }
    [Fact] public void ZeroParasiteInputsPreserveAcceptedResult()
    {
        var previous = EnergyHeatPolicy.Step(new(8, 10), 10, 5, 2, 0.25f, EnergyHeatSettings.Default);
        Assert.Equal(previous, EnergyHeatPolicy.Step(new(8, 10), 10, 5, 2, 0.25f, EnergyHeatSettings.Default, 0, 0));
        Assert.Equal(0, previous.ExternalEnergySpent);
    }
    [Theory] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void InvalidLoadsReject(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyHeatPolicy.Step(default, 10, 5, 0, 1, EnergyHeatSettings.Default, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyHeatPolicy.Step(default, 10, 5, 0, 1, EnergyHeatSettings.Default, 0, invalid));
    }
    [Fact] public void ExtremeFiniteAdditiveLoadsRemainBounded()
    {
        var result = EnergyHeatPolicy.Step(new(10, 10), 10, 5, float.MaxValue, float.MaxValue,
            EnergyHeatSettings.Default, float.MaxValue, float.MaxValue);
        Assert.Equal(new EnergyHeatState(0, 100), result.State); Assert.True(float.IsFinite(result.ExternalEnergySpent));
    }
}
