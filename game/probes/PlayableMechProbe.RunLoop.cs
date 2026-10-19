using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GameFactory.Gameplay.Interaction;
using MechSurvival.Mech;
using MechSurvival.Parasites;
using MechSurvival.Salvage;
using MechSurvival.Shell;

namespace MechSurvival.Probes;

public partial class PlayableMechProbe
{
    private PrototypeRunContent RunContent => _gameplay.GetNode<PrototypeRunContent>("PrototypeRun");
    private SurveyBeacon Beacon => _world.Objects.Select(o => o.Host).OfType<SurveyBeacon>().Single();
    public sealed record RunPacket(SurveyBeacon.Observation View, long BeaconId, string Salvage, string Parasite);
    private async Task RunStage(int stage, string command = "run_observe")
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, JsonSerializer.Serialize(new RunPacket(Beacon.View!, Beacon.NetworkObject.Id.Value, SalvageCapture(), ParasiteCapture())));
        await Until(() => _ack == stage, 60000);
    }
    // Actual owner input traverses the production blockout and normal Netfox simulation.
    private async Task WalkTo(float x, float z)
    {
        ulong end = Time.GetTicksMsec() + 14000;
        while (new Vector2(LocalBody.Position.X - x, LocalBody.Position.Z - z).Length() > 0.35f)
        {
            Require(Time.GetTicksMsec() < end, $"Walk timed out target={x},{z} body={LocalBody.Position}");
            ReleaseInputs();
            float dx = x - LocalBody.Position.X, dz = z - LocalBody.Position.Z;
            if (Math.Abs(dx) > 0.22f) Input.ActionPress(dx > 0 ? "move_right" : "move_left");
            if (Math.Abs(dz) > 0.22f) Input.ActionPress(dz > 0 ? "move_backward" : "move_forward");
            await Ticks(1);
        }
        ReleaseInputs(); await Settle();
    }
    private async Task WalkMainTo(float z, float x = 0)
    {
        while (Math.Abs(LocalBody.Position.Z - z) > 24)
            await WalkTo(0, LocalBody.Position.Z + Math.Sign(z - LocalBody.Position.Z) * 24);
        await WalkTo(x, z);
    }
    private async Task ExplicitServiceAcceptance(bool offline)
    {
        var host = LocalBody; var controller = Service(host);
        var right = host.GetServerPart(MechSlot.RightArm)!;
        string before = host.Composition;
        Require(!controller.TryInstall(new(1), MechSlot.RightArm) && host.Composition == before, "Install with no carried part detached a module.");
        await Pulse("mech_status");
        Require(controller.PanelOpen && !GetTree().Paused, "Tab panel did not open without pausing.");
        controller.RequestDetach(MechSlot.RightArm);
        await Until(() => !host.View.HasPart(MechSlot.RightArm));
        Require(ReferenceEquals(LoosePart(right.Id).GetServerPart(), right), "Explicit detach reconstructed arm.");
        if (!offline) await RunStage(520, "run_arm_absent");
        await PickupServicePart(right.Id);
        before = host.Composition;
        Require(!controller.TryDetach(new(1), MechSlot.RightArm) && host.Composition == before &&
            LoosePart(right.Id).Carry.HolderNetworkObjectId == Object(host).Id.Value, "Detach installed the carried part.");
        var consumedArm = LoosePart(right.Id).NetworkObject.Id;
        controller.RequestInstall(MechSlot.RightArm);
        // Body mutation is immediate; QueueFree unregisters the consumed loose
        // object at frame end. Capture only the completed transfer boundary.
        await Until(() => host.View.HasPart(MechSlot.RightArm) && !_world.TryGet(consumedArm, out _));
        Require(ReferenceEquals(host.GetServerPart(MechSlot.RightArm), right), "Reinstall lost exact arm identity.");
        if (!offline) await RunStage(521, "run_arm_present");
        await Pulse("mech_status"); Require(!controller.PanelOpen, "Panel did not close.");
        GD.Print("RUN_EXPLICIT_SERVICE_PASS missing_install_no_detach=true detach_no_install=true exact_arm=true both_views=true panel_nonpausing=true");
    }
    private void ActivateBeacon() => LocalBody.GetNode<PlayerInteractor>("PlayerInteractor").RequestInteraction(Beacon.NetworkObject.Id);
    private async Task ResetRun(bool offline, int stage)
    {
        var oldGas = Bodies.Select(b => b.Gas).ToArray();
        if (!offline) RpcId(_remote, MethodName.CommandRpc, stage, "run_reset", "");
        await ResetAndCheck();
        if (!offline) await Until(() => _ack == stage, 60000);
        Require(RunContent.Rules is null && _world.Count == 0 && oldGas.All(g => g.ParasiteSourceId == 0), "Run reset retained objects/rules/load.");
    }
    private async Task RunLoopAcceptance(bool offline)
    {
        GD.Print("PLAYABLE_READY role=server");
        if (!offline) await Until(() => _hello);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry(); await Settle();
        Require(RunContent.Rules is { Phase: RunPhase.RunningToObjective, ObjectiveComplete: false } &&
            RunContent.Rules.RemainingSeconds > 595 && Parasites.Length == 0 && Loose.Length == 0 && Bodies.All(b => PrototypeRunContent.InsideExtraction(b.Position)), "Run initial state/content/spawn invalid.");
        var oldBeacon = Beacon.NetworkObject.Id;
        Require(RunContent.DurationSeconds == 600 && Machines.Length == 3 && RunContent.GetNodeOrNull<Node3D>("ObjectiveFacility") is not null, "Authored duration/count/expanded blockout invalid.");
        var machine = Machines[0]; var stored = machine.GetServerRewards().ToArray();
        Require(stored[0].Definition.Model == "industrial_cooling" && stored[1].Definition.Model == "high_density_power", "Run rewards are not useful definitions.");
        Require(stored[0].Definition.Supports(MechSlot.Cooling) && stored[1].Definition.Supports(MechSlot.Power) &&
            Math.Abs(stored[1].Definition.Contributions[PartStat.PowerCapacity] * stored[1].Condition - 12.75f) < 0.001f,
            "Authored improved reward catalog/condition mismatch.");
        if (!offline) await RunStage(501);
        await ExplicitServiceAcceptance(offline);
        await WalkTo(0, -1); await WalkTo(-6, -1);
        StartHack(machine); Require(machine.State == SalvageMachineState.Hacking, "Real salvage interaction failed.");
        await Until(() => machine.InputOpen);
        foreach (int symbol in machine.Sequence.ToArray()) { int before = machine.Progress; await Pulse($"hack_{symbol}"); await Until(() => machine.Progress == before + 1); }
        Require(machine.State == SalvageMachineState.Disabled && stored.All(p => ReferenceEquals(p, LoosePart(p.Id).GetServerPart())), "Salvage reconstructed reward instances.");
        StartHack(machine); Require(!machine.TrySubmit(new(1), 1) && Loose.Length == 2, "Salvage released twice.");
        var host = LocalBody;
        host.Gas.SetResourcesForProbe(new(6, 12));
        var oldCooling = host.GetServerPart(MechSlot.Cooling)!;
        // Both released parts have collision. Leave the gap between machine and
        // power reward via the east side, then approach cooling from the south.
        await WalkTo(-4, -1.4f); await WalkTo(-4, 2.2f);
        await WalkTo(-8.1f, 2.2f); await WalkTo(-8.1f, 1.3f);
        await PickupServicePart(stored[0].Id);
        var consumedCooling = LoosePart(stored[0].Id).NetworkObject.Id;
        Service(LocalBody).RequestInstall(MechSlot.Cooling);
        await Until(() => host.GetPart(MechSlot.Cooling)?.Id == stored[0].Id && !_world.TryGet(consumedCooling, out _));
        Require(ReferenceEquals(host.GetServerPart(MechSlot.Cooling), stored[0]) && ReferenceEquals(LoosePart(oldCooling.Id).GetServerPart(), oldCooling) &&
            Math.Abs(host.Gas.Snapshot.CoolingCapacity - 7.2f) < 0.001f && host.Gas.Snapshot.Energy == 6 && host.Gas.Snapshot.Heat == 12, "Upgrade did not preserve exact transfer/resources or increase canonical cooling.");
        GD.Print("RUN_SALVAGE_PASS exact_instances=true cooling=5->7.2 energy=6 heat=12 no_refill=true single_release=true");
        if (!offline) await RunStage(502);
        // The replaced starter module is now loose beside us; walk around it.
        await WalkTo(-8.1f, 2.2f); await WalkTo(0, 2.2f);
        await WalkMainTo(-31);
        await WalkTo(8.2f, -31);
        var failed = Machines[1]; var lost = failed.GetServerRewards().Select(p => p.Id).ToArray();
        var failedId = failed.NetworkObject.Id;
        StartHack(failed); await Until(() => failed.InputOpen);
        await Pulse($"hack_{failed.Sequence[0] % 4 + 1}");
        await Until(() => failed.State == SalvageMachineState.SelfDestructing);
        Require(failed.DestructSeconds == 3 && failed.GetServerRewards().Count == 0, "Failure countdown/reward loss missing.");
        await Until(() => !_world.TryGet(failedId, out _));
        Require(lost.All(id => Loose.All(p => p.Projection.Id != id)) && failed.CleanedUp, "Failed salvage released rewards or retained state.");
        GD.Print("RUN_SALVAGE_FAILURE_PASS countdown=3 destroyed=true no_loot=true");
        await WalkTo(0, -31);
        await WalkMainTo(-98.1f);
        if (!offline) { await RunStage(503, "run_objective"); }
        else await Pulse("interact");
        await Until(() => RunContent.Rules!.ObjectiveComplete && Parasites.Length == 2);
        var parasite = Parasites.OrderBy(p => p.GlobalPosition.Z).First();
        Require(Bodies.All(b => b.GlobalPosition.DistanceTo(parasite.GlobalPosition) > Parasite.TriggerRadius), "Parasite spawned on a player.");
        ActivateBeacon(); Require(Parasites.Length == 2 && !RunContent.ActivateObjective(), "Objective activated twice.");
        GD.Print("RUN_OBJECTIVE_PASS real_interaction=true one_activation=true parasite_after_objective=true");
        if (!offline) await RunStage(504);
        Vector3 site = parasite.GlobalPosition;
        await WalkTo(site.X, site.Z);
        await Until(() => parasite.State == ParasiteState.Attached && host.Gas.ParasiteSourceId != 0);
        host.Gas.SetExternalHeatLoad(1);
        var resources = host.Gas.Snapshot;
        var pressure = host.Gas.AdvanceResources(1);
        Require(host.Gas.ParasiteSourceId == parasite.NetworkObject.Id.Value && host.Gas.ExternalHeatLoad == 1 &&
            pressure.ExternalEnergySpent > 0 && host.Gas.Snapshot.Energy < resources.Energy &&
            host.Gas.Snapshot.CoolingCapacity == resources.CoolingCapacity && host.Gas.Snapshot.Heat > resources.Heat - 1,
            "Return parasite did not apply canonical pressure alongside improved cooling.");
        CheckParasiteGas(host);
        GD.Print($"RUN_PRESSURE_PASS cooling={host.Gas.Snapshot.CoolingCapacity} energy={resources.Energy}->{host.Gas.Snapshot.Energy} heat={resources.Heat}->{host.Gas.Snapshot.Heat} parasite_drain={pressure.ExternalEnergySpent} native=true");
        if (offline) { StartPurge(parasite); await SolveLocalPurge(parasite); }
        else await RunStage(505, "run_team_purge");
        await Until(() => Parasites.Length == 1);
        parasite = Parasites.Single();
        await WalkMainTo(parasite.GlobalPosition.Z);
        await Until(() => parasite.State == ParasiteState.Attached);
        StartPurge(parasite); await SolveLocalPurge(parasite);
        await Until(() => Parasites.Length == 0);
        Require(host.Gas.ParasiteSourceId == 0, "Purge retained canonical load.");
        if (!offline) await RunStage(506);
        await WalkMainTo(8);
        if (!offline)
        {
            Require(!RunContent.Rules!.Terminal && Beacon.View!.Inside == 1 && Beacon.View.Active == 2, "One player extracted a two-player run.");
            await RunStage(507, "run_extract");
        }
        await Until(() => RunContent.Rules!.Phase == RunPhase.Success);
        if (!offline) await RunStage(508);
        GD.Print($"RUN_SUCCESS_PASS all_active={Bodies.Length} terminal=Success same_result=true");
        long oldMax = _world.Objects.Max(o => o.Id.Value);
        await ResetRun(offline, 509);
        // Authored override only in this harness; normal scene keeps 600 seconds.
        RunContent.DurationSeconds = 0.4;
        StartRound();
        await Until(() => RunContent.Rules!.Phase == RunPhase.Failure);
        Require(!RunContent.ActivateObjective() && !RunContent.Rules!.CheckExtraction(Bodies.Length, Bodies.Length) &&
            RunContent.Rules.RemainingSeconds == 0 && Parasites.Length == 0, "Failure changed after late objective/extraction.");
        if (!offline) await RunStage(510);
        GD.Print("RUN_FAILURE_PASS timer=0 terminal=Failure immutable=true");
        await ResetRun(offline, 511);
        RunContent.DurationSeconds = 600;
        StartRound(); CheckFresh(offline ? 1 : 2); await Settle();
        Require(Beacon.NetworkObject.Id != oldBeacon && _world.Objects.All(o => o.Id.Value > oldMax) &&
            RunContent.Rules is { ObjectiveComplete: false, Phase: RunPhase.RunningToObjective, RemainingSeconds: > 595 } &&
            Parasites.Length == 0 && Loose.Length == 0 && Machines.Length == 3 && Machines.SelectMany(m => m.GetServerRewards()).All(p => stored.All(old => old.Id != p.Id)), "Fresh run retained identities/timer/terminal/content.");
        if (!offline) await RunStage(512);
        await ResetRun(offline, 513);
        if (!offline) { RpcId(_remote, MethodName.FinishRpc); await Until(() => Multiplayer.GetPeers().Length == 0); }
        GD.Print("RUN_ACCEPTANCE_PASS success=true failure=true fresh_run=true world_empty=true transport_same=true");
    }
    private async Task RunLoopCommand(string command, string samples)
    {
        if (command == "run_reset") { await ResetAndCheck(); Require(RunContent.Rules is null && _world.Count == 0, "Client stale run state."); return; }
        var packet = JsonSerializer.Deserialize<RunPacket>(samples)!;
        await Until(() => _world.Objects.Any(o => o.Host is SurveyBeacon) && Beacon.NetworkObject.Id.Value == packet.BeaconId && Beacon.View is { } view && view.Phase == packet.View.Phase &&
            view.ObjectiveComplete == packet.View.ObjectiveComplete && Math.Abs(view.Seconds - packet.View.Seconds) <= 2 && view.Active == packet.View.Active && view.Inside == packet.View.Inside);
        await SalvageCommand("salvage_observe", packet.Salvage);
        await ParasiteCommand("parasite_observe", packet.Parasite);
        bool denied = false;
        try { Beacon.Publish(packet.View with { ObjectiveComplete = true, Phase = RunPhase.Success }); }
        catch (InvalidOperationException) { denied = true; }
        Require(denied, "Client published a canonical run result.");
        if (command == "run_arm_absent")
        {
            Require(!Bodies[0].View.HasPart(MechSlot.RightArm), "Remote View retained detached arm.");
            bool rejected = false;
            try { Service(LocalBody).TryInstall(new(1), MechSlot.Cooling); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Client executed canonical install.");
            Service(Bodies[0]).RequestDetach(MechSlot.Cooling);
            Service(Bodies[0]).RpcId(1, "RequestDetachRpc", (int)MechSlot.Cooling);
            await Ticks(3);
            Require(Bodies[0].View.HasPart(MechSlot.Cooling), "Forged service detached another owner's part.");
        }
        if (command == "run_arm_present") Require(Bodies[0].View.HasPart(MechSlot.RightArm), "Remote View missed reinstalled arm.");
        if (command == "run_objective") { await WalkMainTo(-98.4f, 1.5f); await Pulse("interact"); await Until(() => Beacon.View!.ObjectiveComplete); }
        if (command == "run_team_purge")
        {
            var parasite = Parasites.Single(p => p.State == ParasiteState.Attached);
            await WalkTo(0, parasite.GlobalPosition.Z - 1.8f);
            StartPurge(parasite); await Until(() => parasite.State == ParasiteState.Purging);
            CheckPurgeConfiguration(parasite, true); await SolveLocalPurge(parasite);
        }
        if (command == "run_extract") await WalkMainTo(8, 1.5f);
        GD.Print($"RUN_CLIENT_OBSERVED phase={packet.View.Phase} seconds={packet.View.Seconds} objective={packet.View.ObjectiveComplete} extraction={packet.View.Inside}/{packet.View.Active} command={command}");
    }
}
