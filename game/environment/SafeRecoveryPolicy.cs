using System;
using System.Linq;
using MechSurvival.Mech;

namespace MechSurvival.Environment;

/// <summary>Expedition emergency stabilization; never replaces an installed instance.</summary>
public static class SafeRecoveryPolicy
{
    public const double Delay = 2.5, StabilizationSeconds = 3.5;
    public const float EnergyPerSecond = 1, Ceiling = 0.5f, ConditionStep = 0.05f;
    private static readonly MechSlot[] Slots = { MechSlot.Sensor, MechSlot.LeftLeg, MechSlot.RightLeg, MechSlot.Power, MechSlot.Cooling };
    public static bool Eligible(RoomEnvironment room, int storm, bool infected, float movement) =>
        !room.Active(storm) && !infected && float.IsFinite(movement) && movement <= 0.1f;
    public static MechSlot? Target(MechBodyView view) => Slots
        .Where(s => view.GetPart(s) is { Condition: < Ceiling })
        .OrderBy(s => view.GetPart(s)!.Condition).Select(s => (MechSlot?)s).FirstOrDefault();
    public static float StabilizedCondition(float condition) => Math.Min(Ceiling, condition + ConditionStep);
    public static float RecoveredEnergy(float energy, float capacity, float delta) =>
        Math.Clamp(energy + EnergyPerSecond * delta, 0, capacity);
}
