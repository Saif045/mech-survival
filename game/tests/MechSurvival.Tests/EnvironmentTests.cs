using MechSurvival.Environment;
using MechSurvival.World;
using MechSurvival.Mech;
using MechSurvival.Mech.Gas;
using Xunit;
namespace MechSurvival.Tests;
public class EnvironmentTests
{
    [Theory] [InlineData(120000)] [InlineData(654321)] [InlineData(-1)] [InlineData(0)]
    public void PlansAreDeterministicAndIntersectSalvage(int seed)
    {
        var layout=ExpeditionLayout.Generate(seed);var a=new ExpeditionEnvironmentPolicy(layout);var b=new ExpeditionEnvironmentPolicy(layout);
        Assert.Equal(a.Signature,b.Signature);Assert.Equal(a.Rooms,b.Rooms);
        Assert.Equal(EnvironmentKind.Safe,a.Rooms[0].Kind);Assert.Equal(EnvironmentKind.Safe,a.Rooms[layout.Extraction].Kind);
        Assert.Equal(8,a.Rooms.Count(r=>r.Kind==EnvironmentKind.Safe));Assert.Equal(4,a.Rooms.Count(r=>r.Kind==EnvironmentKind.Thermal));
        Assert.Equal(3,a.Rooms.Count(r=>r.Kind==EnvironmentKind.Electrical));Assert.Equal(3,a.Rooms.Count(r=>r.Kind==EnvironmentKind.Corrosive));
        Assert.All(a.Rooms,r=>Assert.InRange(r.ActivationLevel,0,2));
        Assert.Contains(layout.Salvage,i=>a.Rooms[i].Kind==EnvironmentKind.Thermal);
        Assert.Contains(layout.Salvage,i=>a.Rooms[i].Kind is EnvironmentKind.Electrical or EnvironmentKind.Corrosive);
        Assert.Contains(a.Rooms,r=>r.ActivationLevel==2);
    }
    [Fact] public void IndependentStreamPreservesTask13Geometry()
    {
        var layout=ExpeditionLayout.Generate(120000);var env=new ExpeditionEnvironmentPolicy(layout);
        Assert.Equal("FF0CFD1B7C7D6F38",layout.Signature);
        Assert.Equal(30,Enumerable.Range(0,30).Select(i=>new ExpeditionEnvironmentPolicy(ExpeditionLayout.Generate(i)).Signature).Distinct().Count());
        Assert.NotEqual(env.Signature,new ExpeditionEnvironmentPolicy(layout,20).Signature);
    }
    [Theory] [InlineData(0,0)] [InlineData(119.99,0)] [InlineData(120,1)] [InlineData(240,2)] [InlineData(360,3)] [InlineData(600,3)]
    public void StormBoundaries(double elapsed,int expected)=>Assert.Equal(expected,new ExpeditionEnvironmentPolicy(ExpeditionLayout.Generate(1)).StormLevel(elapsed));
    [Fact] public void IntensitiesAndLoads()
    {
        for(int s=0;s<4;s++)
        {
            if(s>0)Assert.True(ExpeditionEnvironmentPolicy.Intensity(s)>ExpeditionEnvironmentPolicy.Intensity(s-1));
            Assert.Equal((0f,1.5f*ExpeditionEnvironmentPolicy.Intensity(s)),ExpeditionEnvironmentPolicy.Loads(new(EnvironmentKind.Thermal,0),s));
            Assert.Equal((0.08f*ExpeditionEnvironmentPolicy.Intensity(s),0f),ExpeditionEnvironmentPolicy.Loads(new(EnvironmentKind.Electrical,0),s));
            Assert.Equal((0f,0f),ExpeditionEnvironmentPolicy.Loads(new(EnvironmentKind.Safe,0),s));
            Assert.Equal(0.02f*ExpeditionEnvironmentPolicy.Intensity(s),ExpeditionEnvironmentPolicy.CorrosionAmount(s));
        }
        Assert.Equal((0f,0f),ExpeditionEnvironmentPolicy.Loads(new(EnvironmentKind.Thermal,2),1));
        Assert.Equal((0f,0f),ExpeditionEnvironmentPolicy.Loads(new(EnvironmentKind.Electrical,2),0));
    }
    [Fact] public void TargetSelectionSkipsEmptyDestroyedAndNonfunctionalCategories()
    {
        var body=new MechBody();
        foreach(var slot in new[]{MechSlot.Core,MechSlot.LeftLeg,MechSlot.Power})body.AttachPart(slot,new(new PartDefinition(slot.ToString(),new[]{slot})));
        body.ApplyDamage(MechSlot.LeftLeg,1);
        for(int p=0;p<30;p++)Assert.Equal(MechSlot.Power,ExpeditionEnvironmentPolicy.SelectTarget(1,23,p,body.Inspect()));
        body.ApplyDamage(MechSlot.Power,1);Assert.Null(ExpeditionEnvironmentPolicy.SelectTarget(1,23,0,body.Inspect()));
    }
    [Fact] public void TargetSequenceDeterministicAndVaried()
    {
        var body=new MechBody();foreach(var slot in new[]{MechSlot.Sensor,MechSlot.LeftLeg,MechSlot.RightLeg,MechSlot.Power,MechSlot.Cooling})body.AttachPart(slot,new(new PartDefinition(slot.ToString(),new[]{slot})));
        var a=Enumerable.Range(0,30).Select(i=>ExpeditionEnvironmentPolicy.SelectTarget(1,45,i,body.Inspect())).ToArray();
        Assert.Equal(a,Enumerable.Range(0,30).Select(i=>ExpeditionEnvironmentPolicy.SelectTarget(1,45,i,body.Inspect())));Assert.True(a.Distinct().Count()>2);
    }
    [Fact] public void ZeroEnvironmentPreservesPolicyAndContributionsAdd()
    {
        var old=EnergyHeatPolicy.Step(new(10,10),10,5,1,1,EnergyHeatSettings.Default,0.2f,6);
        Assert.Equal(old,EnergyHeatPolicy.Step(new(10,10),10,5,1,1,EnergyHeatSettings.Default,0.2f,6,0,0));
        var combined=EnergyHeatPolicy.Step(new(10,10),10,0,0,1,EnergyHeatSettings.Default,0.2f,6,0.4f,8.1f);
        Assert.Equal(0.6f,combined.ExternalEnergySpent,4);Assert.Equal(14.1f,combined.HeatAdded,4);Assert.Equal(9.3f,combined.State.Energy,4);
    }
    [Theory] [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void InvalidEnvironmentalLoadsReject(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>EnergyHeatPolicy.Step(default,10,5,0,1,EnergyHeatSettings.Default,0,0,value,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>EnergyHeatPolicy.Step(default,10,5,0,1,EnergyHeatSettings.Default,0,0,0,value));
    }
}
