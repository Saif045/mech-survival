using MechSurvival.Mech;
using Xunit;

namespace MechSurvival.Tests;

public class MechBodyTests
{
    private static PartDefinition Leg() => new("leg", [MechSlot.LeftLeg, MechSlot.RightLeg],
        [PartCapability.Mobility], new Dictionary<PartStat, float> { [PartStat.Mobility] = 2 });
    private static PartInstance Part() => new(Leg());

    [Fact] public void EmptyBodyHasAllNineSlots()
    {
        var body = new MechBody();
        Assert.Equal(Enum.GetValues<MechSlot>(), body.Inspect().Slots);
        Assert.Equal(9, body.Inspect().Slots.Count);
        Assert.All(MechBody.Slots, slot => Assert.False(body.HasPart(slot)));
    }
    [Fact] public void ValidAttachKeepsRuntimeIdentity()
    {
        var body = new MechBody(); var part = Part();
        Assert.True(body.AttachPart(MechSlot.LeftLeg, part));
        Assert.Same(part, body.GetPart(MechSlot.LeftLeg));
        Assert.Equal(part.Id, body.Inspect().GetPart(MechSlot.LeftLeg)!.Id);
        Assert.Equal(MechSlot.LeftLeg, part.InstalledSlot);
    }
    [Fact] public void IncompatiblePartIsRejectedWithoutMutation()
    {
        var body = new MechBody(); var part = Part();
        Assert.False(body.AttachPart(MechSlot.Sensor, part));
        Assert.Null(part.InstalledSlot); Assert.Empty(body.Inspect().Parts);
    }
    [Fact] public void DuplicateAndOccupiedSlotAreRejected()
    {
        var body = new MechBody(); var part = Part();
        Assert.True(body.AttachPart(MechSlot.LeftLeg, part));
        Assert.False(body.AttachPart(MechSlot.RightLeg, part));
        Assert.False(body.AttachPart(MechSlot.LeftLeg, part));
        Assert.False(body.AttachPart(MechSlot.LeftLeg, Part()));
        Assert.Single(body.Inspect().Parts);
    }
    [Fact] public void APartCannotOccupyTwoBodies()
    {
        var first = new MechBody(); var second = new MechBody(); var part = Part();
        first.AttachPart(MechSlot.LeftLeg, part);
        Assert.False(second.AttachPart(MechSlot.RightLeg, part));
        first.DetachPart(MechSlot.LeftLeg);
        Assert.True(second.AttachPart(MechSlot.RightLeg, part));
    }
    [Fact] public void ReplacementReturnsOldPartAndFreesIt()
    {
        var body = new MechBody(); var old = Part(); var next = Part();
        body.AttachPart(MechSlot.LeftLeg, old);
        Assert.True(body.ReplacePart(MechSlot.LeftLeg, next, out var previous));
        Assert.Same(old, previous); Assert.Null(old.InstalledSlot);
        Assert.Same(next, body.GetPart(MechSlot.LeftLeg));
        Assert.True(body.AttachPart(MechSlot.RightLeg, old));
    }
    [Fact] public void FailedReplacementPreservesOldPart()
    {
        var body = new MechBody(); var old = Part(); body.AttachPart(MechSlot.LeftLeg, old);
        Assert.False(body.ReplacePart(MechSlot.LeftLeg, old, out var previous));
        Assert.Null(previous); Assert.Same(old, body.GetPart(MechSlot.LeftLeg));
        var sensor = new PartInstance(new PartDefinition("sensor", [MechSlot.Sensor]));
        Assert.False(body.ReplacePart(MechSlot.LeftLeg, sensor, out previous));
        Assert.Null(previous); Assert.Same(old, body.GetPart(MechSlot.LeftLeg));
    }
    [Fact] public void ReplacingEmptySlotInstallsAndReturnsNull()
    {
        var body = new MechBody(); var part = Part();
        Assert.True(body.ReplacePart(MechSlot.LeftLeg, part, out var previous));
        Assert.Null(previous); Assert.Same(part, body.GetPart(MechSlot.LeftLeg));
    }
    [Fact] public void DetachReturnsPartAndEmptyDetachAlwaysReturnsNull()
    {
        var body = new MechBody(); var part = Part(); body.AttachPart(MechSlot.LeftLeg, part);
        Assert.Same(part, body.DetachPart(MechSlot.LeftLeg)); Assert.Null(part.InstalledSlot);
        Assert.Null(body.DetachPart(MechSlot.LeftLeg)); Assert.Null(body.DetachPart(MechSlot.LeftLeg));
        Assert.True(body.AttachPart(MechSlot.RightLeg, part));
    }
    [Theory]
    [InlineData(-0.1f)] [InlineData(1.1f)] [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void InvalidConditionRejectsWithoutChangingState(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartInstance(Leg(), value));
        var body = new MechBody(); body.AttachPart(MechSlot.LeftLeg, Part());
        Assert.Throws<ArgumentOutOfRangeException>(() => body.SetCondition(MechSlot.LeftLeg, value));
        Assert.Equal(1, body.GetPart(MechSlot.LeftLeg)!.Condition);
        Assert.Throws<ArgumentOutOfRangeException>(() => body.SetCondition(MechSlot.RightLeg, value));
    }
    [Fact] public void DestroyedPartRemainsInstalledAndBaseStatsDoNotScale()
    {
        var body = new MechBody(); body.AttachPart(MechSlot.LeftLeg, Part());
        Assert.True(body.SetCondition(MechSlot.LeftLeg, 0));
        Assert.True(body.HasPart(MechSlot.LeftLeg));
        Assert.False(body.Inspect().GetPart(MechSlot.LeftLeg)!.Functioning);
        Assert.Equal(0, body.Inspect().CountCapability(PartCapability.Mobility));
        Assert.Equal(1, body.Inspect().CountCapability(PartCapability.Mobility, false));
        Assert.Equal(2, body.Inspect().SumContribution(PartStat.Mobility));
    }
    [Fact] public void QueriesReflectCompositionAndPreserveUnrelatedSlots()
    {
        var body = new MechBody(); var left = Part(); var right = Part();
        body.AttachPart(MechSlot.LeftLeg, left); body.AttachPart(MechSlot.RightLeg, right);
        var before = body.Inspect();
        Assert.Equal(2, before.CountCapability(PartCapability.Mobility));
        Assert.Equal(4, before.SumContribution(PartStat.Mobility));
        body.SetCondition(MechSlot.LeftLeg, 0.25f); body.DetachPart(MechSlot.LeftLeg);
        Assert.Same(right, body.GetPart(MechSlot.RightLeg)); Assert.Equal(1, right.Condition);
        Assert.Equal(1, body.Inspect().CountCapability(PartCapability.Mobility));
        Assert.Equal(2, body.Inspect().SumContribution(PartStat.Mobility));
        Assert.Equal(1, before.GetPart(MechSlot.LeftLeg)!.Condition); // immutable observation
    }
    [Fact] public void SnapshotRoundTripIsCanonicalAndKeepsIdsAndCondition()
    {
        var definition = Leg(); var body = new MechBody(); var part = new PartInstance(definition, 0.25f);
        body.AttachPart(MechSlot.RightLeg, part);
        var json = body.Inspect().Serialize();
        var mirror = MechBodyView.Deserialize(json, new Dictionary<string, PartDefinition> { ["leg"] = definition });
        Assert.Equal(json, mirror.Serialize()); Assert.Equal(part.Id, mirror.GetPart(MechSlot.RightLeg)!.Id);
        Assert.Equal(0.25f, mirror.GetPart(MechSlot.RightLeg)!.Condition);
    }
    [Fact] public void MalformedSnapshotsAreRejected()
    {
        var definition = Leg(); var id = new MechPartId(Guid.NewGuid());
        var part = new InstalledPart(id, MechSlot.LeftLeg, definition, 1);
        Assert.Throws<ArgumentException>(() => new MechBodyView([part, part]));
        Assert.Throws<ArgumentException>(() => new MechBodyView([part, part with { Slot = MechSlot.RightLeg }]));
        Assert.Throws<ArgumentException>(() => new MechBodyView([part with { Condition = 2 }]));
        Assert.Throws<ArgumentException>(() => new MechBodyView([part with { Slot = MechSlot.Sensor }]));
        Assert.Throws<ArgumentException>(() => new MechBodyView([part with { Id = default }]));
        Assert.Throws<ArgumentException>(() => MechBodyView.Deserialize(new MechBodyView([part]).Serialize(),
            new Dictionary<string, PartDefinition>()));
    }
    [Fact] public void ClearReleasesPartsAndEmptyConditionIsDeterministic()
    {
        var body = new MechBody(); var part = Part(); body.AttachPart(MechSlot.LeftLeg, part);
        body.Clear(); Assert.Empty(body.Inspect().Parts); Assert.Null(part.InstalledSlot);
        Assert.False(body.SetCondition(MechSlot.LeftLeg, 0.5f));
        Assert.True(new MechBody().AttachPart(MechSlot.RightLeg, part));
    }
    [Fact] public void InvalidSlotAndDefinitionAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MechBody().AttachPart((MechSlot)99, Part()));
        Assert.Throws<ArgumentException>(() => new PartDefinition("bad", []));
        Assert.Throws<ArgumentException>(() => new PartDefinition("bad", [(MechSlot)99]));
        Assert.Throws<ArgumentException>(() => new PartDefinition("bad", [MechSlot.Core], contributions:
            new Dictionary<PartStat, float> { [PartStat.Mobility] = float.NaN }));
    }
}
