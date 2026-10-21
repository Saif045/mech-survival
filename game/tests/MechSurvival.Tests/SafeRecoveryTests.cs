using MechSurvival.Environment;
using MechSurvival.Mech;
using MechSurvival.Player;
using Xunit;
namespace MechSurvival.Tests;
public class SafeRecoveryTests
{
    [Theory]
    [InlineData(EnvironmentKind.Safe,0,3,false,0,true)]
    [InlineData(EnvironmentKind.Thermal,2,1,false,0,true)]
    [InlineData(EnvironmentKind.Thermal,2,2,false,0,false)]
    [InlineData(EnvironmentKind.Electrical,0,0,false,0,false)]
    [InlineData(EnvironmentKind.Corrosive,0,3,false,0,false)]
    [InlineData(EnvironmentKind.Safe,0,0,true,0,false)]
    [InlineData(EnvironmentKind.Safe,0,0,false,0.2f,false)]
    public void Eligibility(EnvironmentKind kind,int activation,int storm,bool infected,float motion,bool expected) =>
        Assert.Equal(expected,SafeRecoveryPolicy.Eligible(new(kind,activation),storm,infected,motion));
    [Fact] public void ExpeditionTuningIsForgiving()
    {
        Assert.Equal(0,ExpeditionEnvironmentPolicy.ResourceSettings.BaseEnergyDrainPerSecond);
        Assert.Equal(0.05f,ExpeditionEnvironmentPolicy.ResourceSettings.CoolingEnergyCostPerHeat);
        Assert.Equal(8,ExpeditionEnvironmentPolicy.DamagePulseSeconds);
        Assert.Equal(6,ExpeditionEnvironmentPolicy.OverheatPulseSeconds);
        Assert.Equal(0.02f,ExpeditionEnvironmentPolicy.OverheatDamage);
        Assert.Equal(5,SafeRecoveryPolicy.RecoveredEnergy(4.9f,5,1));
        Assert.Equal(1,SafeRecoveryPolicy.RecoveredEnergy(0,5,1));
    }
    [Fact] public void StabilizationSelectsLowestEligibleAndPreservesIdentity()
    {
        var body=new MechBody();
        foreach(var slot in new[]{MechSlot.Core,MechSlot.Sensor,MechSlot.LeftLeg,MechSlot.Power})
            body.AttachPart(slot,new(new PartDefinition(slot.ToString(),new[]{slot})));
        body.SetCondition(MechSlot.Core,0); body.SetCondition(MechSlot.Sensor,0.49f); body.SetCondition(MechSlot.LeftLeg,0);
        var before=body.Inspect().GetPart(MechSlot.LeftLeg)!;
        Assert.Equal(MechSlot.LeftLeg,SafeRecoveryPolicy.Target(body.Inspect()));
        for(int i=0;i<20;i++) body.SetCondition(MechSlot.LeftLeg,SafeRecoveryPolicy.StabilizedCondition(body.Inspect().GetPart(MechSlot.LeftLeg)!.Condition));
        Assert.Equal(0.5f,body.Inspect().GetPart(MechSlot.LeftLeg)!.Condition);
        Assert.Equal(before.Id,body.Inspect().GetPart(MechSlot.LeftLeg)!.Id);
        Assert.Equal(MechSlot.Sensor,SafeRecoveryPolicy.Target(body.Inspect()));
        body.SetCondition(MechSlot.Sensor,SafeRecoveryPolicy.StabilizedCondition(0.49f));
        Assert.Null(SafeRecoveryPolicy.Target(body.Inspect()));
    }
    [Fact] public void ShortFreshExposureCannotRuinResources()
    {
        foreach(int storm in new[]{0,3}) {
            var state=new MechSurvival.Mech.Gas.EnergyHeatState(10,0);
            for(int i=0;i<60;i++) state=MechSurvival.Mech.Gas.EnergyHeatPolicy.Step(state,10,5,0,0.25f,ExpeditionEnvironmentPolicy.ResourceSettings,environmentalHeatLoad:ExpeditionEnvironmentPolicy.ThermalHeat*ExpeditionEnvironmentPolicy.Intensity(storm)).State;
            Assert.InRange(state.Energy,8,10); Assert.InRange(state.Heat,0,5);
        }
    }
    [Fact] public void DetailOnlyShowsAuthoredContributions()
    {
        var part=new PartDefinition("core",new[]{MechSlot.Core},contributions:new Dictionary<PartStat,float>{{PartStat.Mobility,0},{PartStat.PowerCapacity,0},{PartStat.CoolingCapacity,0}});
        Assert.DoesNotContain("Mobility",MechPartReadout.Describe(part,1));
        Assert.DoesNotContain("Power",MechPartReadout.Describe(part,1));
        Assert.DoesNotContain("Cooling",MechPartReadout.Describe(part,1));
    }
}
