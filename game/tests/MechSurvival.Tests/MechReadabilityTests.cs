using MechSurvival.Mech;
using MechSurvival.Player;
using Xunit;

namespace MechSurvival.Tests;

public class MechReadabilityTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void InstallWithoutCarryNeverBecomesDetach(bool occupied) =>
        Assert.False(MechServiceIntentPolicy.Allows(MechServiceIntent.Install, false, occupied, true));
    [Fact] public void DetachWithCarryNeverBecomesInstall()
    {
        Assert.False(MechServiceIntentPolicy.Allows(MechServiceIntent.Detach, true, false, true));
        Assert.True(MechServiceIntentPolicy.Allows(MechServiceIntent.Detach, true, true, false));
    }
    [Fact] public void InstallRequiresCompatibility()
    {
        Assert.False(MechServiceIntentPolicy.Allows(MechServiceIntent.Install, true, true, false));
        Assert.True(MechServiceIntentPolicy.Allows(MechServiceIntent.Install, true, true, true));
    }
    [Fact] public void ComparisonUsesConditionWeightedPhysicalContribution()
    {
        var starter = new PartDefinition("debug_cooling", [MechSlot.Cooling], [PartCapability.Cooling], new Dictionary<PartStat, float> { [PartStat.CoolingCapacity] = 5 });
        var improved = new PartDefinition("industrial_cooling", [MechSlot.Cooling], [PartCapability.Cooling], new Dictionary<PartStat, float> { [PartStat.CoolingCapacity] = 8 });
        var current = new InstalledPart(new(Guid.NewGuid()), MechSlot.Cooling, starter, 1);
        Assert.Equal(2.2f, MechPartReadout.Difference(current, improved, 0.9f, PartStat.CoolingCapacity), 4);
        Assert.Equal(7.2f, MechPartReadout.Difference(null, improved, 0.9f, PartStat.CoolingCapacity), 4);
        Assert.Contains("7.20", MechPartReadout.Describe(improved, 0.9f));
    }
    [Fact] public void ReadoutDistinguishesEmptyAndInstalledWithoutMutatingView()
    {
        var definition = new PartDefinition("debug_arm", [MechSlot.LeftArm, MechSlot.RightArm]);
        var part = new InstalledPart(new(Guid.NewGuid()), MechSlot.LeftArm, definition, 0.75f);
        var view = new MechBodyView([part]);
        Assert.Equal("EMPTY", MechPartReadout.Installed(view, MechSlot.RightArm));
        Assert.Contains("Debug Arm", MechPartReadout.Installed(view, MechSlot.LeftArm));
        Assert.Equal(part, view.GetPart(MechSlot.LeftArm));
        Assert.Equal("Left Arm", MechPartReadout.SlotName(MechSlot.LeftArm));
        Assert.Equal("", MechPartReadout.Describe(definition, 0.75f));
        Assert.Equal("No physical contribution change", MechPartReadout.Change(part, definition, 1));
    }
}
