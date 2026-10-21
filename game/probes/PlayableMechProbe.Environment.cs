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
    public sealed record EnvironmentBody(long Id, string Composition, float Mobility, float Power, float Cooling, float Energy, float Heat, bool Recovering);
    public sealed record EnvironmentPacket(string Layout, string Signature, int Storm, int Room, EnvironmentBody[] Bodies);
    private async Task EnvironmentStage(int stage, string command = "run_env_observe", int room = 0)
    {
        var packet = new EnvironmentPacket(Expedition.Layout.Signature, Expedition.EnvironmentPlan.Signature, Expedition.View!.StormLevel, room,
            Bodies.Select(b => new EnvironmentBody(Object(b).Id.Value, b.Composition, b.Gas.Snapshot.Mobility, b.Gas.Snapshot.PowerCapacity, b.Gas.Snapshot.CoolingCapacity, b.Gas.Snapshot.Energy, b.Gas.Snapshot.Heat, Expedition.Environment.Exposures.FirstOrDefault(e => e.BodyId == Object(b).Id.Value)?.Recovering ?? false)).ToArray());
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
        await WalkRoom(0);
        if (!offline)
        {
            await EnvironmentStage(600, "run_env_move", thermal);
            await Until(() => Bodies[1].Gas.EnvironmentalHeatLoad > 0);
            Require(host.Gas.EnvironmentalHeatLoad == 0 && host.Gas.EnvironmentalEnergyDrain == 0, "Client thermal room contaminated host.");
            host.Gas.SetResourcesForProbe(new(1,0));
            await Until(()=>host.Gas.Snapshot.Energy>2 && Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(host).Id.Value).Recovering,6000);
            Require(!Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(Bodies[1]).Id.Value).Recovering,"Exposed client recovered.");
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
        await Until(() => before.Any(p => host.GetPart(p.Slot)!.Condition < p.Condition), 10000);
        var changed = before.First(p => host.GetPart(p.Slot)!.Condition < p.Condition);
        Require(host.GetPart(changed.Slot)!.Id == changed.Id && ReferenceEquals(host.GetServerPart(changed.Slot), references[changed.Slot]), "Corrosion replaced installed identity.");
        CheckProjection(host);
        GD.Print($"ENVIRONMENT_CORROSION_PASS slot={changed.Slot} part={changed.Id} condition={host.GetPart(changed.Slot)!.Condition} exact_instance=true gas=true");
        await WalkRoom(0); await Until(() => host.Gas.EnvironmentalHeatLoad == 0 && host.Gas.EnvironmentalEnergyDrain == 0);
        before = host.View.Parts.ToArray(); host.Gas.SetResourceSimulationEnabled(false); host.Gas.SetResourcesForProbe(new(0, 96));
        await Until(() => before.Any(p => host.GetPart(p.Slot)!.Condition < p.Condition), 8000);
        CheckProjection(host); GD.Print("ENVIRONMENT_OVERHEAT_PASS heat=96 sustained=true physical_damage=true");
        // Controlled boundary setup ends the overheat experiment; this is not a gameplay refill/repair.
        host.Gas.SetResourcesForProbe(new(10, 0)); host.Gas.SetResourceSimulationEnabled(true);
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
        await RescueAcceptance(offline);
        await Until(() => Expedition.Environment.StormLevel >= 2, 120000);
        Require(dormant.r.Active(Expedition.Environment.StormLevel), "Storm did not activate dormant room.");
        if (!offline) await EnvironmentStage(606);
        GD.Print($"ENVIRONMENT_STORM_PASS level={Expedition.Environment.StormLevel} dormant_room={dormant.i} now_active=true");
    }
    private async Task RescueAcceptance(bool offline)
    {
        var host = LocalBody;
        await WalkRoom(0);
        // Previous stages deliberately corrode/overheat this body. Start a fresh measurement fixture.
        foreach (var part in host.View.Parts) host.SetCondition(part.Slot,1);
        host.Gas.SetResourcesForProbe(new(10,0));
        Require(host.GasMoveSpeed==6,"Pristine canonical speed changed.");
        var freshStart=host.Position; Input.ActionPress("move_backward"); await Ticks(24); ReleaseInputs(); await Settle();
        CheckDisplacement(host,freshStart,6,"rescue_pristine","server");
        host.Gas.SetResourcesForProbe(new(1,20));
        host.SetCondition(MechSlot.Sensor,0.46f);
        host.SetCondition(MechSlot.LeftLeg,0); host.SetCondition(MechSlot.RightLeg,0);
        var identity = host.GetServerPart(MechSlot.LeftLeg);
        Require(host.GasMoveSpeed == 3.5f,"Emergency floor absent.");
        // Actual rollback displacement with camera yaw set to a quarter turn.
        var camera = host.GetNode("Presentation/Camera3D"); camera.Set("orbit_yaw",Mathf.Pi/2);
        Vector3 start=host.Position;
        Input.ActionPress("move_forward"); await Ticks(24); ReleaseInputs(); await Settle();
        CheckDisplacement(host,start,3.5f,"rescue_emergency","server");
        Require(host.Position.X < start.X-2 && Math.Abs(host.Position.Z-start.Z)<0.3f,"Camera-relative emergency movement failed.");
        camera.Set("orbit_yaw",0f);
        Require(!Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(host).Id.Value).Recovering,"Moving player recovered.");
        if (!offline) {
            var clientStart=Bodies[1].Position;
            await EnvironmentStage(610,"run_env_camera");
            Require(Bodies[1].Position.X < clientStart.X-3,"Server did not receive camera-relative client movement.");
        }
        float energy=host.Gas.Snapshot.Energy, heat=host.Gas.Snapshot.Heat;
        await Until(()=>host.Gas.Snapshot.Energy > energy+0.5f && host.GetPart(MechSlot.LeftLeg)!.Condition>0,12000);
        Require(host.Gas.Snapshot.Heat < heat,"Safe Heat failed to recover.");
        Require(ReferenceEquals(identity,host.GetServerPart(MechSlot.LeftLeg)),"Stabilization replaced instance.");
        Require(host.View.Parts.Where(p=>p.Slot is MechSlot.LeftLeg or MechSlot.RightLeg).All(p=>p.Condition<=0.5f),"Stabilization exceeded ceiling.");
        CheckProjection(host);
        if (!offline) await EnvironmentStage(611);
        // Interrupt an established recovery with real recorded movement, then measure the stopped refill.
        Input.ActionPress("move_right"); await Ticks(6);
        Require(!Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(host).Id.Value).Recovering,"Movement did not interrupt recovery.");
        energy=host.Gas.Snapshot.Energy; await Ticks(12);
        Require(host.Gas.Snapshot.Energy<=energy+0.001f,"Energy recovered while moving.");
        ReleaseInputs(); await Settle();
        // Check ceiling at the boundary without waiting several minutes for all damaged legs.
        host.SetCondition(MechSlot.LeftLeg,0.49f); host.SetCondition(MechSlot.RightLeg,0.5f); host.SetCondition(MechSlot.Sensor,1);
        await Until(()=>host.GetPart(MechSlot.LeftLeg)!.Condition==0.5f,8000);
        await Ticks(120);
        Require(host.GetPart(MechSlot.LeftLeg)!.Condition==0.5f,"Stabilization overshot 50 percent.");
        await Until(()=>host.Gas.Snapshot.Energy==host.Gas.Snapshot.PowerCapacity,12000);
        Require(host.Gas.Snapshot.Energy<=host.Gas.Snapshot.PowerCapacity,"Safe Energy exceeded capacity.");
        var infection=await Infect(host); await Ticks(2);
        Require(!Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(host).Id.Value).Recovering,"Parasite allowed recovery.");
        if (!offline) {
            await Until(()=>Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(Bodies[1]).Id.Value).Recovering,5000);
            await EnvironmentStage(612);
        }
        _world.Despawn(infection.NetworkObject.Id);
        // Restore functional legs before long route validation; recovery itself never restores above 50%.
        foreach(var part in host.View.Parts) host.SetCondition(part.Slot,1);
        host.Gas.SetResourcesForProbe(new(10,0));
        await WalkRoom(HazardRoom(EnvironmentKind.Electrical)); await Ticks(90);
        Require(!Expedition.Environment.Exposures.Single(e=>e.BodyId==Object(host).Id.Value).Recovering,"Active hazard allowed recovery.");
        if (!offline) await EnvironmentStage(613);
        // Restore pristine setup for the full salvage/extraction route.
        foreach(var part in host.View.Parts) host.SetCondition(part.Slot,1);
        host.Gas.SetResourcesForProbe(new(10,0));
        Require(host.GasMoveSpeed==6,"Fresh speed changed.");
        GD.Print("RESCUE_PASS speed_fresh=6 emergency=3.5 camera_relative=true safe_energy=true heat=true stabilization_identity=true ceiling=0.5 moving_stops=true parasite_stops=true hazard_stops=true");
    }
    private async Task EnvironmentCommand(string command, string samples)
    {
        var packet = JsonSerializer.Deserialize<EnvironmentPacket>(samples)!;
        Require(Expedition.Layout.Signature == packet.Layout && Expedition.EnvironmentPlan.Signature == packet.Signature, "Peers disagree on environment plan.");
        if (command == "run_env_camera") {
            var camera=LocalBody.GetNode("Presentation/Camera3D"); camera.Set("orbit_yaw",Mathf.Pi/2);
            var start=LocalBody.Position; Input.ActionPress("move_forward"); await Ticks(24); ReleaseInputs(); await Settle(); camera.Set("orbit_yaw",0f);
            Require(LocalBody.Position.X < start.X-3 && Math.Abs(LocalBody.Position.Z-start.Z)<0.3f,"Client camera-relative movement failed.");
            GD.Print("RESCUE_CLIENT_CAMERA_PASS camera_relative=true owner_prediction=true"); return;
        }
        if (command == "run_env_move") { await WalkRoom(packet.Room); GD.Print($"ENVIRONMENT_CLIENT_MOVED room={packet.Room}"); return; }
        await Until(() => Expedition.View!.StormLevel == packet.Storm);
        foreach (var sample in packet.Bodies)
        {
            var body = Bodies.Single(b => Object(b).Id.Value == sample.Id);
            await Until(() => body.Composition == sample.Composition && Math.Abs(body.Gas.Snapshot.Mobility - sample.Mobility) < 0.0001 && Math.Abs(body.Gas.Snapshot.PowerCapacity - sample.Power) < 0.0001 && Math.Abs(body.Gas.Snapshot.CoolingCapacity - sample.Cooling) < 0.0001);
            CheckProjection(body);
            // Resources continue ticking; compare a bounded live observation rather than an obsolete exact frame.
            if (sample.Recovering) await Until(()=>Math.Abs(body.Gas.Snapshot.Energy-sample.Energy)<2 && Math.Abs(body.Gas.Snapshot.Heat-sample.Heat)<6,3000);
            await Until(()=>Expedition.View!.Exposures!.Any(e=>e.BodyId==sample.Id && e.Recovering==sample.Recovering),3000);
            var exposure = Expedition.View!.Exposures!.Single(e => e.BodyId == sample.Id);
            var expected = ExpeditionEnvironmentPolicy.Loads(Expedition.EnvironmentPlan.Rooms[exposure.Room], packet.Storm);
            Require(exposure.EnergyDrain == expected.Energy && exposure.HeatLoad == expected.Heat, "Client hazard interpretation differs.");
        }
        bool recoveryDenied=false; try { LocalBody.Gas.RecoverSafeEnergy(1); } catch(InvalidOperationException) { recoveryDenied=true; }
        Require(recoveryDenied,"Client recovered resources.");
        bool denied = false; try { LocalBody.Gas.SetEnvironmentalLoads(0, 0); } catch (InvalidOperationException) { denied = true; }
        Require(denied, "Client changed environment loads.");
        GD.Print($"ENVIRONMENT_CLIENT_PASS signature={packet.Signature} storm={packet.Storm} part_id_condition_and_gas=true authority=true");
    }
}
