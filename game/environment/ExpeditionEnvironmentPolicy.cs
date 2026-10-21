using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MechSurvival.Mech.Gas;
using MechSurvival.Mech;
using MechSurvival.World;

namespace MechSurvival.Environment;

public enum EnvironmentKind { Safe, Thermal, Electrical, Corrosive }
public sealed record RoomEnvironment(EnvironmentKind Kind, int ActivationLevel)
{
    public bool Active(int storm) => Kind != EnvironmentKind.Safe && storm >= ActivationLevel;
}

/// <summary>One concrete expedition's tuning, deterministic plan and structural target policy.</summary>
public sealed class ExpeditionEnvironmentPolicy
{
    public const double DefaultStormPhaseSeconds = 120, DamagePulseSeconds = 8, OverheatPulseSeconds = 6;
    public const float ThermalHeat = 1.5f, ElectricalDrain = 0.08f, CorrosionDamage = 0.02f;
    public const float CriticalHeat = 95, OverheatDamage = 0.02f, ExpeditionIdleDrain = 0;
    public static EnergyHeatSettings ResourceSettings => new(ExpeditionIdleDrain, 1, 0.05f);
    private static readonly float[] Intensities = { 0.75f, 1, 1.35f, 1.7f };
    private static readonly string[] Phases = { "CALM", "UNSTABLE", "SEVERE", "CRITICAL" };
    private static readonly MechSlot[] FunctionalSlots = { MechSlot.Sensor, MechSlot.LeftLeg, MechSlot.RightLeg, MechSlot.Power, MechSlot.Cooling };
    public IReadOnlyList<RoomEnvironment> Rooms { get; }
    public double StormPhaseSeconds { get; }
    public string Signature { get; }
    public ExpeditionEnvironmentPolicy(ExpeditionLayout layout, double stormPhaseSeconds = DefaultStormPhaseSeconds)
    {
        if (!double.IsFinite(stormPhaseSeconds) || stormPhaseSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(stormPhaseSeconds));
        StormPhaseSeconds = stormPhaseSeconds;
        uint state = unchecked((uint)layout.Seed) ^ 0xE17A4B39u;
        int Next(int n) { state = unchecked(state * 1664525u + 1013904223u); return (int)((state >> 8) % (uint)n); }
        var rooms = Enumerable.Repeat(new RoomEnvironment(EnvironmentKind.Safe, 0), layout.Rooms.Count).ToArray();
        // Force risk to intersect existing encounters without consuming the layout RNG stream.
        rooms[layout.Salvage[0]] = new(EnvironmentKind.Thermal, 0);
        rooms[layout.Salvage[1]] = new(EnvironmentKind.Corrosive, 0);
        var eligible = Enumerable.Range(0, rooms.Length).Where(i => i != layout.Start && i != layout.Extraction && rooms[i].Kind == EnvironmentKind.Safe).ToArray();
        for (int i = eligible.Length - 1; i > 0; i--) { int j = Next(i + 1); (eligible[i], eligible[j]) = (eligible[j], eligible[i]); }
        var kinds = new[] { EnvironmentKind.Electrical, EnvironmentKind.Thermal, EnvironmentKind.Corrosive,
            EnvironmentKind.Electrical, EnvironmentKind.Thermal, EnvironmentKind.Corrosive, EnvironmentKind.Electrical, EnvironmentKind.Thermal };
        for (int i = 0; i < kinds.Length; i++) rooms[eligible[i]] = new(kinds[i], i == 0 ? 0 : 1 + Next(2));
        Rooms = Array.AsReadOnly(rooms);
        string config = FormattableString.Invariant($"v2|phase={stormPhaseSeconds:R}|heat={ThermalHeat:R}|drain={ElectricalDrain:R}|corrosion={CorrosionDamage:R}|pulse={DamagePulseSeconds:R}|critical={CriticalHeat:R}|overheat={OverheatDamage:R}|idle={ExpeditionIdleDrain:R}|intensity=0.75,1,1.35,1.7|coolingScale=1|coolingCost=0.05|overheatPulse=6|slots=Sensor,LeftLeg,RightLeg,Power,Cooling");
        string data = config + "|" + string.Join(";", rooms.Select(r => $"{(int)r.Kind}:{r.ActivationLevel}"));
        Signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)))[..16];
    }
    public int StormLevel(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
        return (int)Math.Min(3, Math.Floor(elapsed / StormPhaseSeconds));
    }
    public static float Intensity(int storm) => Intensities[Math.Clamp(storm, 0, 3)];
    public static string PhaseName(int storm) => Phases[Math.Clamp(storm, 0, 3)];
    public static (float Energy, float Heat) Loads(RoomEnvironment room, int storm) => !room.Active(storm) ? (0, 0) : room.Kind switch
    {
        EnvironmentKind.Thermal => (0, ThermalHeat * Intensity(storm)),
        EnvironmentKind.Electrical => (ElectricalDrain * Intensity(storm), 0),
        _ => (0, 0)
    };
    public static float CorrosionAmount(int storm) => CorrosionDamage * Intensity(storm);
    public static MechSlot? SelectTarget(int seed, long bodyId, long pulse, MechBodyView view)
    {
        var eligible = FunctionalSlots.Where(s => view.GetPart(s)?.Functioning == true).ToArray();
        if (eligible.Length == 0) return null;
        ulong value = unchecked((ulong)bodyId ^ (uint)seed ^ (ulong)pulse * 0x9E3779B97F4A7C15UL);
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return eligible[(int)((value ^ (value >> 31)) % (ulong)eligible.Length)];
    }
}
