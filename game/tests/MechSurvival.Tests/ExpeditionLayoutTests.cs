using MechSurvival.World;
using Xunit;
namespace MechSurvival.Tests;
public class ExpeditionLayoutTests
{
    [Theory] [InlineData(1)] [InlineData(123456)] [InlineData(-73)] [InlineData(0)]
    public void DeterministicConnectedExpedition(int seed)
    {
        var a=ExpeditionLayout.Generate(seed);var b=ExpeditionLayout.Generate(seed);
        Assert.Equal(a.Signature,b.Signature);Assert.Equal(18,a.Rooms.Count);
        Assert.Equal(18,a.Rooms.Select(r=>(r.X,r.Z)).Distinct().Count());
        Assert.Contains(a.Rooms,r=>r.Connections.Length>=3);
        Assert.Contains(a.Rooms,r=>r.Connections.Length==1);
        Assert.True(a.Rooms.Sum(r=>r.Connections.Length)/2>=a.Rooms.Count);
        foreach(int i in Enumerable.Range(0,a.Rooms.Count))
        {
            Assert.NotEmpty(a.Path(0,i)); Assert.InRange(a.Rooms[i].Template,0,5);
            foreach(int j in a.Rooms[i].Connections)
            {
                Assert.Contains(i,a.Rooms[j].Connections);
                Assert.Equal(1,Math.Abs(a.Rooms[i].X-a.Rooms[j].X)+Math.Abs(a.Rooms[i].Z-a.Rooms[j].Z));
            }
        }
        int far=Enumerable.Range(0,18).Max(i=>a.Path(0,i).Length);
        Assert.True(a.Path(0,a.Extraction).Length>=far-1);Assert.True(far>=4);
        Assert.Equal(5,a.Salvage.Count);Assert.Equal(3,a.Parasites.Count);
        var encounters=a.Salvage.Concat(a.Parasites).ToArray();Assert.Equal(8,encounters.Distinct().Count());
        Assert.All(encounters,i=> { Assert.InRange(i,1,17);Assert.NotEqual(a.Extraction,i); });
    }
    [Fact] public void BroadSeedRangeRetainsNavigationInvariants()
    {
        foreach(int seed in Enumerable.Range(-100,200))
        {
            var layout=ExpeditionLayout.Generate(seed);
            Assert.Contains(layout.Rooms,r=>r.Connections.Length==1);
            Assert.Contains(layout.Rooms,r=>r.Connections.Length>=3);
            Assert.True(layout.Path(0,layout.Extraction).Length>=5);
            Assert.All(Enumerable.Range(0,18),i=>Assert.NotEmpty(layout.Path(0,i)));
        }
    }
    [Fact] public void RepresentativeSeedsDiffer()=>Assert.Equal(40,Enumerable.Range(0,40).Select(i=>ExpeditionLayout.Generate(i).Signature).Distinct().Count());
    [Theory] [InlineData(16)] [InlineData(17)] [InlineData(19)] [InlineData(20)]
    public void RequestedRoomCount(int count)=>Assert.Equal(count,ExpeditionLayout.Generate(42,count).Rooms.Count);
}
