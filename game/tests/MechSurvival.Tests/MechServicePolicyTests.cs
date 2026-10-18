using MechSurvival.Mech;
using Xunit;

namespace MechSurvival.Tests;

public class MechServicePolicyTests
{
    private static PartInstance Leg(float condition = 1) => new(new("leg", [MechSlot.LeftLeg, MechSlot.RightLeg]), condition);

    [Fact] public void EmptySlotQueriesDoNotClaimOrInstallPart()
    {
        var body = new MechBody(); var part = Leg(0.8f);
        Assert.True(body.CanAttachPart(MechSlot.LeftLeg, part));
        Assert.True(body.CanReplacePart(MechSlot.LeftLeg, part));
        Assert.Empty(body.Inspect().Parts); Assert.Null(part.InstalledSlot);
        Assert.True(new MechBody().AttachPart(MechSlot.RightLeg, part));
        Assert.False(body.CanReplacePart(MechSlot.LeftLeg, part));
    }
    [Fact] public void OccupiedSlotQueryPermitsReplacementButNotAttachmentWithoutMutation()
    {
        var body = new MechBody(); var previous = Leg(); var incoming = Leg(0.8f);
        body.AttachPart(MechSlot.LeftLeg, previous);
        string before = body.Inspect().Serialize();
        Assert.False(body.CanAttachPart(MechSlot.LeftLeg, incoming));
        Assert.True(body.CanReplacePart(MechSlot.LeftLeg, incoming));
        Assert.Equal(before, body.Inspect().Serialize()); Assert.Null(incoming.InstalledSlot);
        Assert.True(body.ReplacePart(MechSlot.LeftLeg, incoming, out var displaced));
        Assert.Same(previous, displaced); Assert.Same(incoming, body.GetPart(MechSlot.LeftLeg));
        Assert.Null(previous.InstalledSlot); Assert.Equal(0.8f, incoming.Condition);
    }
    [Fact] public void IncompatibleAndAlreadyInstalledPartsRejectWithoutMutation()
    {
        var body = new MechBody(); var previous = Leg(); body.AttachPart(MechSlot.LeftLeg, previous);
        var other = new PartInstance(new PartDefinition("sensor", [MechSlot.Sensor]));
        string before = body.Inspect().Serialize();
        foreach (var part in new[] { previous, other })
        {
            Assert.False(body.CanReplacePart(MechSlot.LeftLeg, part));
            Assert.False(body.ReplacePart(MechSlot.LeftLeg, part, out var displaced));
            Assert.Null(displaced); Assert.Equal(before, body.Inspect().Serialize());
        }
        Assert.Same(previous, body.GetPart(MechSlot.LeftLeg)); Assert.Null(other.InstalledSlot);
    }
    [Fact] public void DetachReinstallRetainsExactInstanceIdentityAndCondition()
    {
        var body = new MechBody(); var part = Leg(0.8f); var id = part.Id;
        body.AttachPart(MechSlot.LeftLeg, part);
        Assert.Same(part, body.DetachPart(MechSlot.LeftLeg));
        Assert.True(body.CanReplacePart(MechSlot.LeftLeg, part));
        Assert.True(body.ReplacePart(MechSlot.LeftLeg, part, out var displaced));
        Assert.Null(displaced); Assert.Same(part, body.GetPart(MechSlot.LeftLeg));
        Assert.Equal(id, part.Id); Assert.Equal(0.8f, part.Condition);
    }
    [Fact] public void QueryRejectsInvalidSlotWithoutChangingBodyOrPart()
    {
        var body = new MechBody(); var part = Leg();
        Assert.Throws<ArgumentOutOfRangeException>(() => body.CanReplacePart((MechSlot)99, part));
        Assert.Throws<ArgumentOutOfRangeException>(() => body.CanAttachPart((MechSlot)99, part));
        Assert.Empty(body.Inspect().Parts); Assert.Null(part.InstalledSlot);
    }
}
