using System;
using System.Collections.Generic;
using System.Linq;

namespace MechSurvival.Salvage;

public enum SalvageMachineState { Neutral, Hacking, Disabled, SelfDestructing }
public enum SalvageChallengeResult { Pending, Success, Failure }

/// <summary>The one prototype four-symbol challenge, not a minigame framework.</summary>
public sealed class SalvageChallenge
{
    public IReadOnlyList<int> Sequence { get; }
    public int Progress { get; private set; }
    public SalvageChallengeResult Result { get; private set; }
    public SalvageChallenge(IEnumerable<int> sequence)
    {
        var symbols = sequence.ToArray();
        if (symbols.Length != 4 || symbols.Any(s => s is < 1 or > 4))
            throw new ArgumentException("A salvage challenge requires four symbols from 1 to 4.");
        Sequence = Array.AsReadOnly(symbols);
    }
    public bool Submit(int symbol)
    {
        if (Result != SalvageChallengeResult.Pending || symbol is < 1 or > 4) return false;
        if (symbol != Sequence[Progress]) Result = SalvageChallengeResult.Failure;
        else if (++Progress == 4) Result = SalvageChallengeResult.Success;
        return true;
    }
}
