using MechSurvival.Salvage;
using Xunit;

namespace MechSurvival.Tests;

public class SalvageChallengeTests
{
    [Fact] public void CorrectSequenceAdvancesAndSucceedsExactlyOnce()
    {
        var challenge = new SalvageChallenge([3, 1, 4, 2]);
        foreach (int symbol in challenge.Sequence)
        {
            Assert.Equal(SalvageChallengeResult.Pending, challenge.Result);
            Assert.True(challenge.Submit(symbol));
        }
        Assert.Equal(4, challenge.Progress); Assert.Equal(SalvageChallengeResult.Success, challenge.Result);
        Assert.False(challenge.Submit(2)); Assert.False(challenge.Submit(1));
        Assert.Equal(4, challenge.Progress); Assert.Equal(SalvageChallengeResult.Success, challenge.Result);
    }
    [Fact] public void WrongInputFailsAndCannotBeResumed()
    {
        var challenge = new SalvageChallenge([3, 1, 4, 2]);
        Assert.True(challenge.Submit(3)); Assert.True(challenge.Submit(4));
        Assert.Equal(1, challenge.Progress); Assert.Equal(SalvageChallengeResult.Failure, challenge.Result);
        Assert.False(challenge.Submit(1)); Assert.Equal(1, challenge.Progress);
        Assert.Equal(SalvageChallengeResult.Failure, challenge.Result);
    }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(5)] [InlineData(int.MaxValue)]
    public void InvalidSymbolDoesNotAdvanceOrFail(int symbol)
    {
        var challenge = new SalvageChallenge([1, 2, 3, 4]);
        Assert.False(challenge.Submit(symbol)); Assert.Equal(0, challenge.Progress);
        Assert.Equal(SalvageChallengeResult.Pending, challenge.Result);
    }
    [Fact] public void SequenceIsValidatedAndCopied()
    {
        int[] symbols = [1, 2, 3, 4]; var challenge = new SalvageChallenge(symbols); symbols[0] = 4;
        Assert.Equal(1, challenge.Sequence[0]);
        Assert.Throws<ArgumentException>(() => new SalvageChallenge([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => new SalvageChallenge([1, 2, 3, 5]));
    }
}
