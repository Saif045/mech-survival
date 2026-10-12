using MechSurvival.Mech;
using MechSurvival.Mech.Gas;
using Xunit;

namespace MechSurvival.Tests;

public class EnergyHeatTests
{
    private static EnergyHeatStepResult Step(EnergyHeatState state, float power = 100, float cooling = 0,
        float load = 0, float delta = 1, float drain = 0, float scale = 1, float cost = 1) =>
        EnergyHeatPolicy.Step(state, power, cooling, load, delta, new(drain, scale, cost));
    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 0.00001f, expected + 0.00001f);

    [Fact] public void FullInitializationUsesCurrentCapacityAndKeepsHeat()
    {
        Assert.Equal(new EnergyHeatState(60, 20), EnergyHeatPolicy.InitializeFull(new(5, 20), 60));
        Assert.Equal(new EnergyHeatState(0, 20), EnergyHeatPolicy.InitializeFull(new(5, 20), 0));
    }
    [Fact] public void CapacityReductionClampsAndCapacityIncreaseDoesNotRefill()
    {
        Assert.Equal(60, Step(new(80, 0), power: 60, delta: 0).State.Energy);
        Assert.Equal(45, Step(new(45, 0), power: 100, delta: 0).State.Energy);
        Assert.Equal(0, Step(new(80, 20), power: 0).State.Energy);
    }
    [Fact] public void IdleDrainAndExternalLoadAreProportionalToElapsedTime()
    {
        var result = Step(new(100, 10), load: 8, delta: 0.5f, drain: 2);
        Near(99, result.State.Energy); Near(14, result.State.Heat); Near(1, result.BaseEnergySpent); Near(4, result.HeatAdded);
    }
    [Fact] public void CoolingCostsOnlyTheHeatActuallyRemoved()
    {
        var result = Step(new(100, 3), cooling: 10, cost: 2);
        Near(0, result.State.Heat); Near(3, result.HeatRemoved); Near(6, result.CoolingEnergySpent); Near(94, result.State.Energy);
        var cold = Step(new(100, 0), cooling: 10, drain: 1, cost: 2);
        Near(99, cold.State.Energy); Assert.Equal(0, cold.HeatRemoved); Assert.Equal(0, cold.CoolingEnergySpent);
    }
    [Fact] public void IdleDrainOccursBeforeCoolingAndRemainingEnergyLimitsCooling()
    {
        var result = Step(new(5, 20), cooling: 10, drain: 1, cost: 2);
        Near(0, result.State.Energy); Near(18, result.State.Heat);
        Near(1, result.BaseEnergySpent); Near(2, result.HeatRemoved); Near(4, result.CoolingEnergySpent);
    }
    [Theory] [InlineData(0, 10)] [InlineData(100, 0)]
    public void NoEnergyOrNoCoolingPreventsCooling(float energy, float cooling)
    {
        var result = Step(new(energy, 10), cooling: cooling, load: 8);
        Near(18, result.State.Heat); Assert.Equal(0, result.HeatRemoved); Assert.Equal(0, result.CoolingEnergySpent);
    }
    [Fact] public void IncomingHeatSaturatesBeforeCoolingAndZeroLoadCanCool()
    {
        var saturated = Step(new(100, 98), cooling: 5, load: 1000);
        Near(95, saturated.State.Heat); Near(2, saturated.HeatAdded); Near(5, saturated.HeatRemoved);
        var cooling = Step(new(100, 10), cooling: 5, delta: 0.5f);
        Near(7.5f, cooling.State.Heat); Near(97.5f, cooling.State.Energy);
    }
    [Fact] public void LargeFiniteRatesAndDeltaCannotEscapeBoundsOrOverflow()
    {
        var result = Step(new(float.MaxValue, 50), power: float.MaxValue, cooling: float.MaxValue,
            load: float.MaxValue, delta: float.MaxValue, drain: float.MaxValue, scale: float.MaxValue, cost: float.MaxValue);
        Assert.Equal(new EnergyHeatState(0, 100), result.State);
        Assert.True(float.IsFinite(result.BaseEnergySpent)); Assert.True(float.IsFinite(result.HeatRemoved));
        var affordable = Step(new(1, 100), cooling: float.MaxValue, delta: float.MaxValue, scale: float.MaxValue, cost: float.Epsilon);
        Assert.InRange(affordable.State.Energy, 0, 1); Assert.InRange(affordable.State.Heat, 0, 100);
    }
    [Fact] public void StableBoundariesAndZeroDeltaAreDeterministic()
    {
        var state = new EnergyHeatState(0, 0);
        Assert.Equal(state, Step(state, power: 0, cooling: 10, drain: 100).State);
        Assert.Equal(new EnergyHeatState(0, 100), Step(new(0, 100), cooling: 10, load: 100).State);
        var inputs = Step(new(30, 40), cooling: 5, load: 8, delta: 0.25f, drain: 0.1f);
        Assert.Equal(inputs, Step(new(30, 40), cooling: 5, load: 8, delta: 0.25f, drain: 0.1f));
        Assert.Equal(new EnergyHeatState(30, 40), Step(new(30, 40), cooling: 5, load: 8, delta: 0).State);
    }
    [Fact] public void DamagedStructureFeedsLowerCapacityAndCoolingRate()
    {
        var body = new MechBody();
        body.AttachPart(MechSlot.Power, new PartInstance(new PartDefinition("power", [MechSlot.Power], [PartCapability.Power],
            new Dictionary<PartStat, float> { [PartStat.PowerCapacity] = 100 })));
        body.AttachPart(MechSlot.Cooling, new PartInstance(new PartDefinition("cool", [MechSlot.Cooling], [PartCapability.Cooling],
            new Dictionary<PartStat, float> { [PartStat.CoolingCapacity] = 10 })));
        var before = MechGasSnapshot.FromStructure(body.Inspect());
        body.ApplyDamage(MechSlot.Power, 0.5f); body.ApplyDamage(MechSlot.Cooling, 0.5f);
        var after = MechGasSnapshot.FromStructure(body.Inspect());
        var original = Step(new(80, 0), before.PowerCapacity, before.CoolingCapacity, load: 12);
        var damaged = Step(new(80, 0), after.PowerCapacity, after.CoolingCapacity, load: 12);
        Near(2, original.State.Heat); Near(7, damaged.State.Heat); Near(45, damaged.State.Energy);
        Assert.True(damaged.State.Energy < original.State.Energy);
    }
    [Theory] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void InvalidValuesRejectAtEveryInputAndConfigurationBoundary(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatState(invalid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatState(0, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyHeatPolicy.InitializeFull(default, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(default, power: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(default, cooling: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(default, load: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(default, delta: invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatSettings(invalid, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatSettings(0, invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatSettings(0, 1, invalid));
    }
    [Fact] public void InvalidHeatOrFreeCoolingConfigurationRejects()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatState(0, 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnergyHeatSettings(0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyHeatPolicy.Step(default, 0, 0, 0, 0, default));
    }
    [Fact] public void ResourceSnapshotRoundTripPreservesCanonicalValuesAndTags()
    {
        var state = new MechGasSnapshot(3, 60, 10, true, true, true, true, 37, 68, false, false);
        Assert.Equal(state, MechGasSnapshot.Deserialize(state.Serialize()));
        Assert.Equal(MechGasSnapshot.ColdEmpty, MechGasSnapshot.Deserialize(MechGasSnapshot.ColdEmpty.Serialize()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MechGasSnapshot(0, 10, 0, false, false, true, false, 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MechGasSnapshot(0, 10, 0, false, false, true, false, 1, 101));
    }
}
