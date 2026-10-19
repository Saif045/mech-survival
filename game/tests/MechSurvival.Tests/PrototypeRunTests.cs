using MechSurvival.Shell;
using Xunit;

namespace MechSurvival.Tests;

public class PrototypeRunTests
{
    [Fact] public void DeadlineClampsAndFailureCannotChange()
    {
        var run = new PrototypeRunRules(1); run.Advance(5); run.Advance(1);
        Assert.Equal(0, run.RemainingSeconds); Assert.Equal(RunPhase.Failure, run.Phase);
        Assert.False(run.ActivateObjective()); Assert.False(run.CheckExtraction(2, 2));
    }
    [Fact] public void ObjectiveTransitionsExactlyOnce()
    {
        var run = new PrototypeRunRules(120);
        Assert.True(run.ActivateObjective()); Assert.False(run.ActivateObjective());
        Assert.Equal(RunPhase.ReturningToExtraction, run.Phase);
    }
    [Theory] [InlineData(false, 2, 2)] [InlineData(true, 2, 1)] [InlineData(true, 0, 0)]
    public void ExtractionRejectsIncompleteRuns(bool objective, int active, int inside)
    {
        var run = new PrototypeRunRules(120); if (objective) run.ActivateObjective();
        Assert.False(run.CheckExtraction(active, inside)); Assert.False(run.Terminal);
    }
    [Fact] public void SuccessIsImmutable()
    {
        var run = new PrototypeRunRules(120); run.ActivateObjective(); Assert.True(run.CheckExtraction(2, 2));
        run.Advance(200); Assert.Equal(RunPhase.Success, run.Phase); Assert.False(run.ActivateObjective());
    }
}
