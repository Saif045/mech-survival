using MechSurvival.Mech;
using Xunit;

namespace MechSurvival.Tests;

public class MechDamageTests
{
    private static PartDefinition Leg() => new("leg", [MechSlot.LeftLeg, MechSlot.RightLeg],
        [PartCapability.Mobility], new Dictionary<PartStat, float> { [PartStat.Mobility] = 2 });
    private static MechBody Body(float condition = 1)
    {
        var body = new MechBody();
        body.AttachPart(MechSlot.LeftLeg, new PartInstance(Leg(), condition));
        return body;
    }
    private static void Near(float expected, float actual) => Assert.InRange(actual, expected - 0.000001f, expected + 0.000001f);

    [Fact] public void PartialDamageReportsTheActualChangeAndKeepsPartIdentity()
    {
        var body = Body(); var part = body.GetPart(MechSlot.LeftLeg)!;
        var result = body.ApplyDamage(MechSlot.LeftLeg, 0.25f);
        Assert.Equal(part.Id, result.PartId); Assert.Equal(MechSlot.LeftLeg, result.Slot);
        Near(1, result.PreviousCondition); Near(0.75f, result.NewCondition);
        Near(0.25f, result.RequestedDamage); Near(0.25f, result.AppliedDamage);
        Assert.True(result.Changed); Assert.False(result.DestroyedByThisOperation);
        Assert.Same(part, body.GetPart(MechSlot.LeftLeg)); Assert.Equal(MechSlot.LeftLeg, part.InstalledSlot);
    }
    [Theory] [InlineData(0.2f)] [InlineData(0.5f)] [InlineData(float.MaxValue)]
    public void DamageSaturatesAtZeroWithoutDetaching(float amount)
    {
        var body = Body(0.2f); var part = body.GetPart(MechSlot.LeftLeg)!;
        var result = body.ApplyDamage(MechSlot.LeftLeg, amount);
        Assert.True(result.DestroyedByThisOperation); Assert.True(result.Changed);
        Assert.Equal(0, result.NewCondition); Near(0.2f, result.AppliedDamage);
        Assert.Equal(amount, result.RequestedDamage); Assert.Same(part, body.GetPart(MechSlot.LeftLeg));
        Assert.Equal(2, body.Inspect().SumContribution(PartStat.Mobility));
        Assert.Equal(0, body.Inspect().SumEffectiveContribution(PartStat.Mobility));
        Assert.Equal(0, body.Inspect().CountCapability(PartCapability.Mobility));
        Assert.Equal(1, body.Inspect().CountCapability(PartCapability.Mobility, false));
    }
    [Theory] [InlineData(-0.1f)] [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void InvalidDamageRejectsInstalledAndEmptyTargetsWithoutMutation(float amount)
    {
        var body = Body(); string before = body.Inspect().Serialize();
        Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyDamage(MechSlot.LeftLeg, amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyDamage(MechSlot.RightLeg, amount));
        Assert.Equal(before, body.Inspect().Serialize());
    }
    [Fact] public void EmptySlotReturnsAnExplicitStableNoChange()
    {
        var body = new MechBody(); var result = body.ApplyDamage(MechSlot.LeftLeg, 0.5f);
        Assert.Equal(result, body.ApplyDamage(MechSlot.LeftLeg, 0.5f));
        Assert.Null(result.PartId); Assert.Equal(MechSlot.LeftLeg, result.Slot);
        Assert.Equal(0.5f, result.RequestedDamage); Assert.Equal(0, result.AppliedDamage);
        Assert.False(result.Changed); Assert.False(result.DestroyedByThisOperation); Assert.Empty(body.Inspect().Parts);
    }
    [Theory] [InlineData(0)] [InlineData(float.Epsilon)]
    public void UnrepresentableOrZeroChangeKeepsTheOriginalState(float amount)
    {
        var body = Body(); string before = body.Inspect().Serialize();
        var result = body.ApplyDamage(MechSlot.LeftLeg, amount);
        Assert.False(result.Changed); Assert.Equal(0, result.AppliedDamage);
        Assert.False(result.DestroyedByThisOperation); Assert.Equal(before, body.Inspect().Serialize());
    }
    [Fact] public void AlreadyDestroyedPartIsStable()
    {
        var body = Body(0); var result = body.ApplyDamage(MechSlot.LeftLeg, 0.25f);
        Assert.NotNull(result.PartId); Assert.False(result.Changed); Assert.False(result.DestroyedByThisOperation);
        Assert.Equal(0, result.NewCondition); Assert.Equal(0, result.AppliedDamage); Assert.True(body.HasPart(MechSlot.LeftLeg));
    }
    [Theory] [InlineData(1, 2)] [InlineData(0.5f, 1)] [InlineData(0.1f, 0.2f)] [InlineData(0, 0)]
    public void EffectiveOutputIsLinearWhileRawOutputIsPreserved(float condition, float effective)
    {
        var view = Body(condition).Inspect(); var part = view.GetPart(MechSlot.LeftLeg)!;
        Near(2, part.GetContribution(PartStat.Mobility)); Near(effective, part.GetEffectiveContribution(PartStat.Mobility));
        Near(2, view.SumContribution(PartStat.Mobility)); Near(effective, view.SumEffectiveContribution(PartStat.Mobility));
        Assert.Equal(condition > 0, part.Functioning);
    }
    [Fact] public void CapabilityIsAvailableAtAnyPositiveConditionAndDisappearsOnlyAtZero()
    {
        var sensor = new PartDefinition("sensor", [MechSlot.Sensor], [PartCapability.Sensor]);
        var body = new MechBody(); body.AttachPart(MechSlot.Sensor, new PartInstance(sensor, 0.2f));
        Assert.Equal(1, body.Inspect().CountCapability(PartCapability.Sensor));
        body.SetCondition(MechSlot.Sensor, float.Epsilon);
        Assert.Equal(1, body.Inspect().CountCapability(PartCapability.Sensor));
        body.ApplyDamage(MechSlot.Sensor, float.Epsilon);
        Assert.Equal(0, body.Inspect().CountCapability(PartCapability.Sensor)); Assert.True(body.HasPart(MechSlot.Sensor));
    }
    [Fact] public void MultiplePartialOutputsAggregateAndDamageLeavesUnrelatedPartsUntouched()
    {
        var body = Body(); var right = new PartInstance(Leg(), 0.9f); body.AttachPart(MechSlot.RightLeg, right);
        var before = body.Inspect(); body.ApplyDamage(MechSlot.LeftLeg, 0.5f);
        var after = body.Inspect(); Near(4, after.SumContribution(PartStat.Mobility));
        Near(2.8f, after.SumEffectiveContribution(PartStat.Mobility));
        Assert.Equal(before.GetPart(MechSlot.RightLeg), after.GetPart(MechSlot.RightLeg));
        Assert.Same(right, body.GetPart(MechSlot.RightLeg)); Near(1, before.GetPart(MechSlot.LeftLeg)!.Condition);
        Near(3.8f, before.SumEffectiveContribution(PartStat.Mobility));
    }
    [Fact] public void RepeatedDamageHasTheSameConditionSequenceAndDestroysOnce()
    {
        var first = Body(); var second = Body(); int destroyed = 0;
        foreach (var amount in new[] { 0.1f, 0.25f, 0.4f, 0.5f, 0.1f })
        {
            var a = first.ApplyDamage(MechSlot.LeftLeg, amount); var b = second.ApplyDamage(MechSlot.LeftLeg, amount);
            Assert.Equal(a.NewCondition, b.NewCondition); Assert.Equal(a.AppliedDamage, b.AppliedDamage);
            if (a.DestroyedByThisOperation) destroyed++;
        }
        Assert.Equal(1, destroyed); Assert.Equal(0, first.GetPart(MechSlot.LeftLeg)!.Condition);
    }
    [Fact] public void DamagedSerializationKeepsConditionAndDerivedOutput()
    {
        var body = Body(); body.ApplyDamage(MechSlot.LeftLeg, 0.65f);
        var view = body.Inspect(); string json = view.Serialize();
        var mirror = MechBodyView.Deserialize(json, new Dictionary<string, PartDefinition> { ["leg"] = Leg() });
        Assert.Equal(json, mirror.Serialize()); Assert.Equal(view.GetPart(MechSlot.LeftLeg)!.Condition, mirror.GetPart(MechSlot.LeftLeg)!.Condition);
        Assert.Equal(view.SumEffectiveContribution(PartStat.Mobility), mirror.SumEffectiveContribution(PartStat.Mobility));
        Assert.Equal(view.CountCapability(PartCapability.Mobility), mirror.CountCapability(PartCapability.Mobility));
    }
    [Fact] public void UnknownSlotRejectsRatherThanCreatingStructure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Body().ApplyDamage((MechSlot)99, 0.25f));
    }
}
