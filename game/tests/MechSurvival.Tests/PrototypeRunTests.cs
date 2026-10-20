using MechSurvival.Shell;
using Xunit;

namespace MechSurvival.Tests;

public class PrototypeRunTests
{
    private static bool Complete(PrototypeRunRules run) { run.RecordSalvage(1,true);run.RecordSalvage(2,true);return run.RecordSalvage(3,true); }
    [Fact] public void FailedAndDuplicateMachinesDoNotUnlock()
    {
        var run=new PrototypeRunRules(600);
        Assert.False(run.RecordSalvage(1,false));Assert.Equal(0,run.SalvageCount);
        Assert.False(run.RecordSalvage(1,true));Assert.False(run.RecordSalvage(1,true));
        Assert.False(run.RecordSalvage(2,true));Assert.False(run.ObjectiveComplete);
        Assert.True(run.RecordSalvage(3,true));Assert.False(run.RecordSalvage(4,true));Assert.Equal(3,run.SalvageCount);
    }

    [Fact] public void DeadlineClampsAndFailureCannotChange()
    {
        var run = new PrototypeRunRules(1); run.Advance(5); run.Advance(1);
        Assert.Equal(0, run.RemainingSeconds); Assert.Equal(RunPhase.Failure, run.Phase);
        Assert.False(Complete(run)); Assert.False(run.CheckExtraction(2, 2));
    }
    [Fact] public void ObjectiveTransitionsExactlyOnce()
    {
        var run = new PrototypeRunRules(120);
        Assert.True(Complete(run)); Assert.False(Complete(run));
        Assert.Equal(RunPhase.ReturningToExtraction, run.Phase);
    }
    [Theory] [InlineData(false, 2, 2)] [InlineData(true, 2, 1)] [InlineData(true, 0, 0)]
    public void ExtractionRejectsIncompleteRuns(bool objective, int active, int inside)
    {
        var run = new PrototypeRunRules(120); if (objective) Complete(run);
        Assert.False(run.CheckExtraction(active, inside)); Assert.False(run.Terminal);
    }
    [Fact] public void SuccessIsImmutable()
    {
        var run = new PrototypeRunRules(120); Complete(run); Assert.True(run.CheckExtraction(2, 2));
        run.Advance(200); Assert.Equal(RunPhase.Success, run.Phase); Assert.False(Complete(run));
    }
}
