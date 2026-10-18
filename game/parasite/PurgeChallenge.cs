using System;

namespace MechSurvival.Parasites;

public enum ParasiteState { Lurking, Attached, Purging, Expelled }

/// <summary>One bespoke discrete timing sweep. No clocks or networking in the rules.</summary>
public sealed class PurgeChallenge
{
    public static PurgeChallenge Self() => new(8, 3, 4, 3);
    public static PurgeChallenge Teammate() => new(8, 2, 5, 2);
    public int Steps { get; }
    public int SafeStart { get; }
    public int SafeEnd { get; }
    public int RequiredHits { get; }
    public int Cursor { get; private set; }
    public int Progress { get; private set; }
    public bool Armed { get; private set; } = true;
    public bool Succeeded => Progress == RequiredHits;
    public bool IsSafe => Cursor >= SafeStart && Cursor <= SafeEnd;
    public PurgeChallenge(int steps, int safeStart, int safeEnd, int requiredHits)
    {
        if (steps < 2 || safeStart < 0 || safeEnd < safeStart || safeEnd >= steps || safeEnd - safeStart + 1 >= steps || requiredHits < 1)
            throw new ArgumentOutOfRangeException(nameof(steps), "A purge needs a bounded safe zone, an outside re-arm zone and positive hits.");
        Steps = steps; SafeStart = safeStart; SafeEnd = safeEnd; RequiredHits = requiredHits;
    }
    public void Advance()
    {
        if (Succeeded) return;
        Cursor = (Cursor + 1) % Steps;
        if (!IsSafe) Armed = true;
    }
    public bool Pulse()
    {
        if (Succeeded || !Armed) return false;
        if (!IsSafe) { Progress = 0; return false; }
        Armed = false; Progress++;
        return true;
    }
}
