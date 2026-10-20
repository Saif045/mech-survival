using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MechSurvival.Environment;
using MechSurvival.Mech;
using MechSurvival.Mech.Gas;
using MechSurvival.Parasites;
namespace MechSurvival.Probes;
public partial class PlayableMechProbe
{
    public sealed record EnvironmentBody(long Id, string Composition, float Mobility, float Power, float Cooling);
    public sealed record EnvironmentPacket(string Layout, string Signature, int Storm, int Room, EnvironmentBody[] Bodies);
    private async Task EnvironmentStage(int stage, string command = "run_env_observe", int room = 0)
    {
        var packet = new EnvironmentPacket(Expedition.Layout.Signature, Expedition.EnvironmentPlan.Signature, Expedition.View!.StormLevel, room,
            Bodies.Select(b => new EnvironmentBody(Object(b).Id.Value, b.Composition, b.Gas.Snapshot.Mobility, b.Gas.Snapshot.PowerCapacity, b.Gas.Snapshot.CoolingCapacity)).ToArray());
        RpcId(_remote, MethodName.CommandRpc, stage, command, JsonSerializer.Serialize(packet));
        await Until(() => _ack == stage, 120000);
    }
    private int HazardRoom(EnvironmentKind kind) => Enumerable.Range(0, Expedition.Layout.Rooms.Count).First(i => Expedition.EnvironmentPlan.Rooms[i] is { ActivationLevel: 0 } r && r.Kind == kind);
    private void CheckProjection(MechSurvival.Player.PlayableMech body)
    {
        var expected = MechGasSnapshot.FromStructure(body.View); var actual = body.Gas.Snapshot;
        Require(Math.Abs(expected.Mobility - actual.Mobility) < 0.0001 && Math.Abs(expected.PowerCapacity - actual.PowerCapacity) < 0.0001 && Math.Abs(expected.CoolingCapacity - actual.CoolingCapacity) < 0.0001, "Environmental damage did not reconcile canonical GAS.");
        Require(body.Gas.ReadSnapshot() == actual, "Environmental resource state bypassed native ASC.");
    }
    private async Task SolveEnvironmentalPurge(Parasite parasite)
    {
        var id = parasite.NetworkObject.Id; int required = parasite.RequiredHits;
        for (int hit = 0; hit < required; hit++)
        {
            await Until(() => parasite.Armed && parasite.Cursor == 3);
            await Pulse("purge_pulse");
            if (hit == required - 1) await Until(() => !_world.TryGet(id, out _));
            else await Until(() => parasite.Progress == hit + 1);
        }
    }
    private async Task EnvironmentAcceptance(bool offline)
    {
        Require(Expedition.Layout.Signature == "FF0CFD1B7C7D6F38", "Task13 layout stream changed.");
        foreach (var parasite in Parasites.ToArray()) _world.Despawn(parasite.NetworkObject.Id);
        await Until(() => Parasites.Length == 0);
        foreach (var body in Bodies) body.Gas.SetResourceSimulationEnabled(true);
        int thermal = HazardRoom(EnvironmentKind.Thermal), electrical = HazardRoom(EnvironmentKind.Electrical), corrosive = HazardRoom(EnvironmentKind.Corrosive);
        var dormant = Expedition.EnvironmentPlan.Rooms.Select((r, i) => (r, i)).First(p => p.r.ActivationLevel == 2);
        GD.Print($"ENVIRONMENT_PLAN seed={Expedition.Seed} layout={Expedition.Layout.Signature} environment={Expedition.EnvironmentPlan.Signature} thermal={thermal} electrical={electrical} corrosive={corrosive} dormant={dormant.i} phase_seconds={Expedition.EnvironmentPlan.StormPhaseSeconds}");
        Require(!dormant.r.Active(0), "Dormant hazard active in calm.");
        var host = LocalBody;
        if (!offline)
        {
            await EnvironmentStage(600, "run_env_move", thermal);
            await Until(() => Bodies[1].Gas.EnvironmentalHeatLoad > 0);
            Require(host.Gas.EnvironmentalHeatLoad == 0 && host.Gas.EnvironmentalEnergyDrain == 0, "Client thermal room contaminated host.");
            await EnvironmentStage(601);
            GD.Print("ENVIRONMENT_SPLIT_THERMAL_PASS host=Safe client=Thermal only_client_loaded=true");
            await EnvironmentStage(602, "run_env_move", corrosive);
        }
        await WalkRoom(thermal); await Until(() => host.Gas.EnvironmentalHeatLoad > 0);
        float energy = host.Gas.Snapshot.Energy;
        var manual = host.Gas.AdvanceResources(0.25f);
        Require(manual.HeatAdded > 0 && host.Gas.Snapshot.Energy <= energy, "Thermal load did not reach canonical simulation.");
        float heat = host.Gas.Snapshot.Heat;
        await WalkRoom(0); await Until(() => host.Gas.EnvironmentalHeatLoad == 0 && host.Gas.EnvironmentalEnergyDrain == 0);
        // A setter must only affect future work, regardless of the cooling performed while walking.
        var resource = host.Gas.Snapshot; host.Gas.SetEnvironmentalLoads(0, 0); Require(host.Gas.Snapshot == resource, "Safe room reset resources.");
        GD.Print($"ENVIRONMENT_THERMAL_SAFE_PASS heat_sample={heat} safe_heat={host.Gas.Snapshot.Heat} no_refill=true");
        await WalkRoom(electrical); await Until(() => host.Gas.EnvironmentalEnergyDrain > 0);
        host.Gas.SetResourcesForProbe(new(5, 0)); energy = host.Gas.Snapshot.Energy;
        await Until(() => host.Gas.Snapshot.Energy < energy);
        Require(host.Gas.EnvironmentalHeatLoad == 0, "Electrical room applied thermal input.");
        GD.Print("ENVIRONMENT_ELECTRICAL_PASS canonical_energy_decreased=true");
        if (!offline)
        {
            await Until(() => Bodies[1].Gas.EnvironmentalEnergyDrain == 0 && Expedition.RoomAt(Bodies[1].GlobalPosition) == corrosive);
            await EnvironmentStage(603);
            GD.Print("ENVIRONMENT_SPLIT_CORROSION_PASS host=Electrical client=Corrosive per_body=true");
            await EnvironmentStage(604, "run_env_move", 0);
        }
        await WalkRoom(corrosive);
        var before = host.View.Parts.ToArray(); var references = before.ToDictionary(p => p.Slot, p => host.GetServerPart(p.Slot));
        await Until(() => before.Any(p => host.GetPart(p.Slot)!.Condition < p.Condition), 5000);
        var changed = before.First(p => host.GetPart(p.Slot)!.Condition < p.Condition);
        Require(host.GetPart(changed.Slot)!.Id == changed.Id && ReferenceEquals(host.GetServerPart(changed.Slot), references[changed.Slot]), "Corrosion replaced installed identity.");
        CheckProjection(host);
        GD.Print($"ENVIRONMENT_CORROSION_PASS slot={changed.Slot} part={changed.Id} condition={host.GetPart(changed.Slot)!.Condition} exact_instance=true gas=true");
        await WalkRoom(0); await Until(() => host.Gas.EnvironmentalHeatLoad == 0 && host.Gas.EnvironmentalEnergyDrain == 0);
        before = host.View.Parts.ToArray(); host.Gas.SetResourcesForProbe(new(0, 96));
        await Until(() => before.Any(p => host.GetPart(p.Slot)!.Condition < p.Condition), 5000);
        CheckProjection(host); GD.Print("ENVIRONMENT_OVERHEAT_PASS heat=96 sustained=true physical_damage=true");
        // Controlled boundary setup ends the overheat experiment; this is not a gameplay refill/repair.
        host.Gas.SetResourcesForProbe(new(10, 0));
        await WalkRoom(thermal); await Until(() => host.Gas.EnvironmentalHeatLoad > 0);
        var infection = await Infect(host); float environment = host.Gas.EnvironmentalHeatLoad;
        var step = host.Gas.AdvanceResources(0.25f);
        Require(host.Gas.ParasiteHeatLoad == 6 && step.HeatAdded > 0 && host.Gas.EnvironmentalHeatLoad == environment, "Parasite/environment failed to compose.");
        if (!offline) await EnvironmentStage(605);
        StartPurge(infection); await Until(() => infection.State == ParasiteState.Purging); await SolveEnvironmentalPurge(infection);
        Require(host.Gas.ParasiteSourceId == 0 && host.Gas.EnvironmentalHeatLoad > 0, "Purge cleared environment.");
        var remaining = await Infect(host);
        await WalkRoom(0); await Until(() => host.Gas.EnvironmentalHeatLoad == 0);
        Require(host.Gas.ParasiteSourceId == remaining.NetworkObject.Id.Value, "Room exit cleared parasite.");
        StartPurge(remaining); await Until(() => remaining.State == ParasiteState.Purging); await SolveEnvironmentalPurge(remaining);
        GD.Print("ENVIRONMENT_COMPOSITION_PASS purge_preserves_environment=true exit_preserves_parasite=true resource_simulation_live=true");
        await Until(() => Expedition.Environment.StormLevel >= 2, 120000);
        Require(dormant.r.Active(Expedition.Environment.StormLevel), "Storm did not activate dormant room.");
        if (!offline) await EnvironmentStage(606);
        GD.Print($"ENVIRONMENT_STORM_PASS level={Expedition.Environment.StormLevel} dormant_room={dormant.i} now_active=true");
    }
    private async Task EnvironmentCommand(string command, string samples)
    {
        var packet = JsonSerializer.Deserialize<EnvironmentPacket>(samples)!;
        Require(Expedition.Layout.Signature == packet.Layout && Expedition.EnvironmentPlan.Signature == packet.Signature, "Peers disagree on environment plan.");
        if (command == "run_env_move") { await WalkRoom(packet.Room); GD.Print($"ENVIRONMENT_CLIENT_MOVED room={packet.Room}"); return; }
        await Until(() => Expedition.View!.StormLevel == packet.Storm);
        foreach (var sample in packet.Bodies)
        {
            var body = Bodies.Single(b => Object(b).Id.Value == sample.Id);
            await Until(() => body.Composition == sample.Composition && Math.Abs(body.Gas.Snapshot.Mobility - sample.Mobility) < 0.0001 && Math.Abs(body.Gas.Snapshot.PowerCapacity - sample.Power) < 0.0001 && Math.Abs(body.Gas.Snapshot.CoolingCapacity - sample.Cooling) < 0.0001);
            CheckProjection(body);
            var exposure = Expedition.View!.Exposures!.Single(e => e.BodyId == sample.Id);
            var expected = ExpeditionEnvironmentPolicy.Loads(Expedition.EnvironmentPlan.Rooms[exposure.Room], packet.Storm);
            Require(exposure.EnergyDrain == expected.Energy && exposure.HeatLoad == expected.Heat, "Client hazard interpretation differs.");
        }
        bool denied = false; try { LocalBody.Gas.SetEnvironmentalLoads(0, 0); } catch (InvalidOperationException) { denied = true; }
        Require(denied, "Client changed environment loads.");
        GD.Print($"ENVIRONMENT_CLIENT_PASS signature={packet.Signature} storm={packet.Storm} part_id_condition_and_gas=true authority=true");
    }
}
