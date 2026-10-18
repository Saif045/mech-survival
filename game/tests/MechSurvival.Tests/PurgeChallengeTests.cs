using MechSurvival.Parasites;
using Xunit;

namespace MechSurvival.Tests;

public class PurgeChallengeTests
{
    private static void Safe(PurgeChallenge challenge)
    {
        do { challenge.Advance(); } while (!challenge.IsSafe);
    }
    [Fact] public void HitInsideWindowAdvances()
    {
        var challenge = PurgeChallenge.Self(); Safe(challenge);
        Assert.True(challenge.Pulse()); Assert.Equal(1, challenge.Progress); Assert.False(challenge.Armed);
    }
    [Fact] public void OutsidePulseResetsProgress()
    {
        var challenge = PurgeChallenge.Self(); Safe(challenge); challenge.Pulse();
        while (challenge.IsSafe) challenge.Advance();
        Assert.False(challenge.Pulse()); Assert.Equal(0, challenge.Progress); Assert.True(challenge.Armed);
    }
    [Fact] public void SameSafePassCannotScoreTwiceEvenAtAnotherSafeStep()
    {
        var challenge = PurgeChallenge.Self(); Safe(challenge); challenge.Pulse();
        Assert.False(challenge.Pulse()); challenge.Advance();
        Assert.True(challenge.IsSafe); Assert.False(challenge.Pulse()); Assert.Equal(1, challenge.Progress);
    }
    [Fact] public void LeavingWindowRearmsForNextPass()
    {
        var challenge = PurgeChallenge.Self(); Safe(challenge); challenge.Pulse();
        while (challenge.IsSafe) challenge.Advance();
        Assert.True(challenge.Armed); Safe(challenge); Assert.True(challenge.Pulse()); Assert.Equal(2, challenge.Progress);
    }
    [Fact] public void RequiredHitsSucceedOnceAndTerminalChallengeCannotResume()
    {
        var challenge = PurgeChallenge.Self();
        for (int hit = 0; hit < 3; hit++)
        {
            Safe(challenge); Assert.True(challenge.Pulse());
            if (hit != 2) while (challenge.IsSafe) challenge.Advance();
        }
        int cursor = challenge.Cursor; Assert.True(challenge.Succeeded);
        Assert.False(challenge.Pulse()); challenge.Advance(); Assert.Equal(cursor, challenge.Cursor); Assert.Equal(3, challenge.Progress);
    }
    [Fact] public void TeammateHasWiderWindowAndFewerRequiredHits()
    {
        var self = PurgeChallenge.Self(); var team = PurgeChallenge.Teammate();
        Assert.True(team.SafeStart < self.SafeStart && team.SafeEnd > self.SafeEnd);
        Assert.True(team.RequiredHits < self.RequiredHits); Assert.Equal(self.Steps, team.Steps);
    }
    [Theory]
    [InlineData(1, 0, 0, 1)] [InlineData(8, -1, 3, 1)] [InlineData(8, 4, 3, 1)]
    [InlineData(8, 3, 8, 1)] [InlineData(8, 0, 7, 1)] [InlineData(8, 3, 4, 0)]
    public void InvalidConfigurationRejected(int steps, int start, int end, int hits) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PurgeChallenge(steps, start, end, hits));
}
