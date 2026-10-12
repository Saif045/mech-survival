using MechSurvival.Mech;
using MechSurvival.Mech.Gas;
using Xunit;

namespace MechSurvival.Tests;

public class MechGasSnapshotTests
{
    private static PartDefinition Leg() => new("leg", [MechSlot.LeftLeg, MechSlot.RightLeg],
        [PartCapability.Mobility], new Dictionary<PartStat, float> { [PartStat.Mobility] = 2 });

    [Fact] public void StructuralInputUsesEffectiveValuesAndAggregateCapabilityAvailability()
    {
        var body = new MechBody(); body.AttachPart(MechSlot.LeftLeg, new PartInstance(Leg(), 0.5f));
        body.AttachPart(MechSlot.RightLeg, new PartInstance(Leg()));
        body.AttachPart(MechSlot.Power, new PartInstance(new PartDefinition("power", [MechSlot.Power],
            [PartCapability.Power], new Dictionary<PartStat, float> { [PartStat.PowerCapacity] = 10 }), 0.5f));
        body.AttachPart(MechSlot.Cooling, new PartInstance(new PartDefinition("cool", [MechSlot.Cooling],
            [PartCapability.Cooling], new Dictionary<PartStat, float> { [PartStat.CoolingCapacity] = 4 }), 0.25f));
        var input = MechGasSnapshot.FromStructure(body.Inspect());
        Assert.Equal(new MechGasSnapshot(3, 5, 1, true, false, true, true), input);
        body.ApplyDamage(MechSlot.LeftLeg, 1);
        Assert.True(MechGasSnapshot.FromStructure(body.Inspect()).HasMobility);
        body.ApplyDamage(MechSlot.RightLeg, 1);
        Assert.False(MechGasSnapshot.FromStructure(body.Inspect()).HasMobility);
        Assert.Equal(3, input.Mobility); // previous projection stays immutable
    }
    [Fact] public void EquivalentStructureProducesEquivalentInputDespitePartIdentityOrRawDestroyedOutput()
    {
        var first = new MechBody(); var second = new MechBody();
        first.AttachPart(MechSlot.LeftLeg, new PartInstance(Leg(), 0));
        Assert.Equal(MechGasSnapshot.FromStructure(first.Inspect()), MechGasSnapshot.FromStructure(second.Inspect()));
        first.AttachPart(MechSlot.RightLeg, new PartInstance(Leg(), 0.5f));
        second.AttachPart(MechSlot.LeftLeg, new PartInstance(Leg(), 0.5f));
        Assert.Equal(MechGasSnapshot.FromStructure(first.Inspect()), MechGasSnapshot.FromStructure(second.Inspect()));
    }
    [Fact] public void SensorAvailabilityUsesPositiveConditionWithoutStatOrThresholdRules()
    {
        var body = new MechBody(); body.AttachPart(MechSlot.Sensor,
            new PartInstance(new PartDefinition("sensor", [MechSlot.Sensor], [PartCapability.Sensor]), float.Epsilon));
        Assert.True(MechGasSnapshot.FromStructure(body.Inspect()).HasSensor);
        Assert.Equal(0, MechGasSnapshot.FromStructure(body.Inspect()).Mobility);
        body.ApplyDamage(MechSlot.Sensor, float.Epsilon);
        Assert.False(MechGasSnapshot.FromStructure(body.Inspect()).HasSensor);
    }
    [Fact] public void CanonicalSnapshotRoundTripsAndCanDifferFromItsPhysicalInput()
    {
        var state = new MechGasSnapshot(3.5f, 10, 5, true, true, true, true);
        string json = state.Serialize();
        Assert.Equal(state, MechGasSnapshot.Deserialize(json)); Assert.Equal(json, MechGasSnapshot.Deserialize(json).Serialize());
        Assert.Equal(default, MechGasSnapshot.Deserialize(default(MechGasSnapshot).Serialize()));
        // Serialization transports GAS results, not a body-derived reconstruction.
        Assert.NotEqual(MechGasSnapshot.FromStructure(new MechBody().Inspect()), state);
    }
    [Theory] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void InvalidAttributeValuesRejectInEveryField(float invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MechGasSnapshot(invalid, 0, 0, false, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MechGasSnapshot(0, invalid, 0, false, false, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MechGasSnapshot(0, 0, invalid, false, false, false, false));
    }
    [Fact] public void InvalidSerializedAttributesReject()
    {
        string json = new MechGasSnapshot(1, 0, 0, true, false, false, false).Serialize();
        Assert.Throws<ArgumentOutOfRangeException>(() => MechGasSnapshot.Deserialize(json.Replace("\"Mobility\":1", "\"Mobility\":-1")));
        Assert.Throws<System.Text.Json.JsonException>(() => MechGasSnapshot.Deserialize("null"));
    }
}
