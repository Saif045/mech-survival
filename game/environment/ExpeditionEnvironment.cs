using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MechSurvival.Mech;
using MechSurvival.Mech.Gas;
using MechSurvival.Player;
using MechSurvival.Shell;

namespace MechSurvival.Environment;

/// <summary>Server exposure/timers only. GAS and NetworkMechBody retain all mutable mech state.</summary>
public sealed class ExpeditionEnvironment
{
    public sealed record Exposure(long BodyId, int Room, EnvironmentKind Kind, bool Active, float EnergyDrain, float HeatLoad, string Damage);
    private sealed class Timing
    {
        public required PlayableMech Body;
        public double Corrosion, Critical, FeedbackUntil;
        public long Pulse;
        public string Feedback = "";
    }
    private readonly ExpeditionRun _run;
    private readonly Dictionary<long, Timing> _timing = new();
    public double Elapsed { get; private set; }
    public int StormLevel => _run.EnvironmentPlan.StormLevel(Elapsed);
    public Exposure[] Exposures { get; private set; } = Array.Empty<Exposure>();
    public ExpeditionEnvironment(ExpeditionRun run) => _run = run;
    public void Advance(double delta, PlayableMech[] bodies, bool terminal)
    {
        if (!_run.Multiplayer.IsServer()) throw new InvalidOperationException("Environment is server-owned.");
        if (terminal) { Clear(); return; }
        Elapsed += delta;
        var live = bodies.Select(b => b.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id.Value).ToHashSet();
        foreach (long id in _timing.Keys.Where(id => !live.Contains(id)).ToArray()) { ClearBody(_timing[id].Body); _timing.Remove(id); }
        var exposures = new List<Exposure>();
        foreach (var body in bodies)
        {
            long id = body.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id.Value;
            if (!_timing.TryGetValue(id, out var timing))
            {
                _timing[id] = timing = new() { Body = body };
                body.Gas.ConfigureResources(ExpeditionEnvironmentPolicy.ResourceSettings);
            }
            int room = _run.RoomAt(body.GlobalPosition);
            var plan = _run.EnvironmentPlan.Rooms[room];
            var loads = ExpeditionEnvironmentPolicy.Loads(plan, StormLevel);
            body.Gas.SetEnvironmentalLoads(loads.Energy, loads.Heat);
            // Bound catch-up to one pulse/frame. Leaving exposure discards partial corrosion time.
            timing.Corrosion = plan.Active(StormLevel) && plan.Kind == EnvironmentKind.Corrosive ? Math.Min(ExpeditionEnvironmentPolicy.DamagePulseSeconds, timing.Corrosion + delta) : 0;
            timing.Critical = body.Gas.Snapshot.Heat >= ExpeditionEnvironmentPolicy.CriticalHeat ? Math.Min(ExpeditionEnvironmentPolicy.DamagePulseSeconds, timing.Critical + delta) : 0;
            if (timing.Corrosion >= ExpeditionEnvironmentPolicy.DamagePulseSeconds)
            {
                timing.Corrosion = 0; Damage(id, timing, ExpeditionEnvironmentPolicy.CorrosionAmount(StormLevel), "CORROSION");
            }
            if (timing.Critical >= ExpeditionEnvironmentPolicy.DamagePulseSeconds)
            {
                timing.Critical = 0; Damage(id, timing, ExpeditionEnvironmentPolicy.OverheatDamage, "OVERHEAT");
            }
            exposures.Add(new(id, room, plan.Kind, plan.Active(StormLevel), loads.Energy, loads.Heat, Elapsed < timing.FeedbackUntil ? timing.Feedback : ""));
        }
        Exposures = exposures.ToArray();
    }
    private void Damage(long id, Timing timing, float amount, string cause)
    {
        var slot = ExpeditionEnvironmentPolicy.SelectTarget(_run.Seed, id, timing.Pulse++, timing.Body.View);
        if (slot is null) return;
        var result = timing.Body.ApplyDamage(slot.Value, amount);
        if (!result.Changed) return;
        timing.Feedback = $"{cause} — {slot.Value.ToString().ToUpperInvariant().Replace("LEFTLEG", "LEFT LEG").Replace("RIGHTLEG", "RIGHT LEG")} {timing.Body.GetPart(slot.Value)!.Condition:P0}";
        timing.FeedbackUntil = Elapsed + 3;
        GD.Print($"ENVIRONMENT_DAMAGE body={id} cause={cause} slot={slot} part={timing.Body.GetPart(slot.Value)!.Id} condition={timing.Body.GetPart(slot.Value)!.Condition:R}");
    }
    private static void ClearBody(PlayableMech body)
    {
        if (GodotObject.IsInstanceValid(body) && body.IsInsideTree() && !body.Gas.CleanedUp) body.Gas.SetEnvironmentalLoads(0, 0);
    }
    public void Clear()
    {
        foreach (var timing in _timing.Values) ClearBody(timing.Body);
        _timing.Clear(); Exposures = Array.Empty<Exposure>();
    }
}
