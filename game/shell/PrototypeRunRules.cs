using System;

namespace MechSurvival.Shell;

public enum RunPhase { RunningToObjective, ReturningToExtraction, Success, Failure }

/// <summary>Only the deadline and one-way transitions for this authored run.</summary>
public sealed class PrototypeRunRules
{
    public RunPhase Phase { get; private set; } = RunPhase.RunningToObjective;
    public double RemainingSeconds { get; private set; }
    public bool ObjectiveComplete { get; private set; }
    public bool Terminal => Phase is RunPhase.Success or RunPhase.Failure;
    public PrototypeRunRules(double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        RemainingSeconds = duration;
    }
    public void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (Terminal) return;
        RemainingSeconds = Math.Max(0, RemainingSeconds - delta);
        if (RemainingSeconds == 0) Phase = RunPhase.Failure;
    }
    public bool ActivateObjective()
    {
        if (Terminal || ObjectiveComplete) return false;
        ObjectiveComplete = true; Phase = RunPhase.ReturningToExtraction; return true;
    }
    public bool CheckExtraction(int active, int inside)
    {
        if (Terminal || !ObjectiveComplete || active <= 0 || inside != active) return false;
        Phase = RunPhase.Success; return true;
    }
}
