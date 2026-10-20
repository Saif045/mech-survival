using System;
using System.Collections.Generic;
namespace MechSurvival.Shell;
public enum RunPhase { RunningToObjective, ReturningToExtraction, Success, Failure }
public sealed class PrototypeRunRules
{
    public RunPhase Phase { get; private set; } = RunPhase.RunningToObjective;
    public double RemainingSeconds { get; private set; }
    public int SalvageCount => _successful.Count;
    public bool ObjectiveComplete => SalvageCount >= 3;
    public bool Terminal => Phase is RunPhase.Success or RunPhase.Failure;
    private readonly HashSet<long> _successful = new();
    public PrototypeRunRules(double duration) { if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration)); RemainingSeconds = duration; }
    public void Advance(double delta) { if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta)); if (Terminal) return; RemainingSeconds = Math.Max(0, RemainingSeconds - delta); if (RemainingSeconds == 0) Phase = RunPhase.Failure; }
    public bool RecordSalvage(long machineId, bool disabled)
    {
        if (Terminal || ObjectiveComplete || !disabled || machineId <= 0 || !_successful.Add(machineId)) return false;
        if (!ObjectiveComplete) return false;
        Phase = RunPhase.ReturningToExtraction; return true;
    }
    public bool CheckExtraction(int active, int inside) { if (Terminal || !ObjectiveComplete || active <= 0 || inside != active) return false; Phase = RunPhase.Success; return true; }
}
