using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MechSurvival.Parasites;
using MechSurvival.Salvage;
using MechSurvival.Shell;
using MechSurvival.Environment;
namespace MechSurvival.Probes;
public partial class PlayableMechProbe
{
    private PrototypeRunContent RunContent => _gameplay.GetNode<PrototypeRunContent>("PrototypeRun");
    private ExpeditionRun Expedition => _world.Objects.Select(o => o.Host).OfType<ExpeditionRun>().Single();
    public sealed record ThreatSample(long Id, int SpawnRoom, float X, float Y, float Z, long Target, long Attached);
    public sealed record RunPacket(int Seed, string Signature, int Connections, ExpeditionRun.Observation View, long Id, string Placements, long ParasiteId, ThreatSample[] Threats);
    private string Placements() => string.Join(";", Machines.Select(m => $"{m.NetworkObject.Id}:{m.GlobalPosition}"));
    private async Task RunStage(int stage, string command = "run_observe", Parasite? parasite = null)
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, JsonSerializer.Serialize(new RunPacket(Expedition.Seed, Expedition.Layout.Signature, Expedition.Layout.Rooms.Sum(r => r.Connections.Length), Expedition.View!, Expedition.NetworkObject.Id.Value, Placements(), parasite?.NetworkObject.Id.Value ?? 0, Parasites.Select(p => new ThreatSample(p.NetworkObject.Id.Value, Expedition.RoomAt(p.InitialPosition), p.GlobalPosition.X, p.GlobalPosition.Y, p.GlobalPosition.Z, p.ChaseTargetId, p.AttachedNetworkObjectId)).ToArray())));
        // The farthest extraction route spans nine 42m sectors; allow degraded client travel.
        await Until(() => _ack == stage, command == "run_extract" ? 180000UL : 60000UL);
    }
    private async Task WalkTo(float x, float z)
    {
        float distance=new Vector2(LocalBody.Position.X-x,LocalBody.Position.Z-z).Length();
        ulong allowance=(ulong)Math.Max(14000,1000*(distance/Math.Max(0.5f,LocalBody.GasMoveSpeed)*2+4));
        ulong end = Time.GetTicksMsec() + allowance;
        while (new Vector2(LocalBody.Position.X - x, LocalBody.Position.Z - z).Length() > 0.35f)
        {
            Require(Time.GetTicksMsec() < end, $"Walk timeout {x},{z} body={LocalBody.Position}"); ReleaseInputs();
            float dx = x - LocalBody.Position.X, dz = z - LocalBody.Position.Z;
            if (Math.Abs(dx) > 0.22f) Input.ActionPress(dx > 0 ? "move_right" : "move_left");
            if (Math.Abs(dz) > 0.22f) Input.ActionPress(dz > 0 ? "move_backward" : "move_forward");
            await Ticks(1);
        }
        ReleaseInputs(); await Settle();
    }
    private async Task WalkRoom(int room)
    {
        foreach (int next in Expedition.Layout.Path(Expedition.RoomAt(LocalBody.GlobalPosition), room)) { var p = Expedition.Center(next);
            // Fixed per-owner lanes keep a waiting teammate off the other route's waypoints.
            float lane=Multiplayer.IsServer()?-1.2f:1.2f;
            await WalkTo(p.X+lane, p.Z+lane); }
    }
    private async Task ResetRun(bool offline, int stage)
    {
        var gas = Bodies.Select(b => b.Gas).ToArray(); if (!offline) RpcId(_remote, MethodName.CommandRpc, stage, "run_reset", "");
        await ResetAndCheck(); if (!offline) await Until(() => _ack == stage, 60000);
        Require(RunContent.Rules is null && _world.Count == 0 && gas.All(g => g.ParasiteSourceId == 0 && g.EnvironmentalHeatLoad == 0 && g.EnvironmentalEnergyDrain == 0), "Reset retained state.");
    }
    private async Task RunLoopAcceptance(bool offline)
    {
        GD.Print("PLAYABLE_READY role=server"); if (!offline) await Until(() => _hello);
        // Diagnostic travel visits hazards repeatedly across enlarged sectors. Normal product deadline stays 600.
        RunContent.DurationSeconds = 1200; RunContent.StormPhaseSeconds = 20; RunContent.ProbeSeed = 120000; StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry();
        // Capture placements before the first simulation frame advances roaming.
        Require(Expedition.Layout.Rooms.Count == 18 && Machines.Length == 5 && Parasites.Length == 3 && Loose.Length == 0, "Expedition composition invalid.");
        Require(Machines.Select(m => Expedition.RoomAt(m.GlobalPosition)).SequenceEqual(Expedition.Layout.Salvage), "Salvage assignment mismatch.");
        Require(Parasites.Select(p => Expedition.RoomAt(p.GlobalPosition)).SequenceEqual(Expedition.Layout.Parasites), "Parasite assignment mismatch.");
        string firstEnvironmentSignature = Expedition.EnvironmentPlan.Signature;
        string firstSignature = Expedition.Layout.Signature; int firstSeed = Expedition.Seed; long oldId = Expedition.NetworkObject.Id.Value;
        var rewards = Machines.SelectMany(m => m.GetServerRewards()).Select(p => p.Id).ToArray();
        await Settle(); if (!offline) await RunStage(501);
        GD.Print($"RUN_LAYOUT_PASS seed={Expedition.Seed} signature={Expedition.Layout.Signature} rooms=18 salvage=5 parasites=3 extraction_distance={Expedition.Layout.Path(0, Expedition.Layout.Extraction).Length - 1}");
        // Approach the nearest threat along real collision routes, then let it cover multiple connections.
        var threat = Parasites.OrderBy(p => Expedition.Layout.Path(0, Expedition.RoomAt(p.GlobalPosition)).Length).First();
        int threatRoom = Expedition.RoomAt(threat.GlobalPosition);
        var path = Expedition.Layout.Path(0, threatRoom);
        if (path.Length > 4) await WalkRoom(path[path.Length - 4]);
        await Until(() => threat.ChaseTargetId != 0, 20000);
        var chaseStart = threat.GlobalPosition;
        await Until(() => threat.GlobalPosition.DistanceTo(chaseStart) > 2 && threat.State == ParasiteState.Lurking, 10000);
        if (!offline) await RunStage(503, "run_chase_motion", threat);
        await Until(() => Parasites.Any(p => p.State == ParasiteState.Attached), 45000);
        var attached = Parasites.First(p => p.State == ParasiteState.Attached);
        Require(attached.TraversedConnections >= 2, "Parasite did not traverse multiple connections.");
        var infected = Bodies.Single(b => Object(b).Id.Value == attached.AttachedNetworkObjectId);
        CheckParasiteGas(infected);
        if (offline) { StartPurge(attached); await SolveLocalPurge(attached); }
        else if (Object(infected).OwnerPeerId.Value == 1) await RunStage(502, "run_team_purge", attached);
        else { await WalkRoom(Expedition.RoomAt(infected.GlobalPosition)); await WalkTo(infected.GlobalPosition.X - 1.8f, infected.GlobalPosition.Z); StartPurge(attached); await Until(() => attached.State == ParasiteState.Purging); CheckPurgeConfiguration(attached, true); await SolveLocalPurge(attached); }
        GD.Print("RUN_CHASE_PASS graph_connections=multiple detection=true catch=true purge=true");
        await EnvironmentAcceptance(offline);
        int stage = 510;
        foreach (var machine in Machines.Take(3).ToArray())
        {
            await WalkRoom(Expedition.RoomAt(machine.GlobalPosition));
            // Authored salvage bay at (+4,+4), approach its south side without crossing the machine.
            await WalkTo(machine.GlobalPosition.X, machine.GlobalPosition.Z - 2);
            StartHack(machine); await HackSuccess(machine, true, 0);
            await Until(() => RunContent.Rules!.SalvageCount == stage - 509);
            if (!offline)
            {
                Require(Bodies.Select(b => Expedition.RoomAt(b.GlobalPosition)).Distinct().Count() > 1, "Players did not split into different rooms.");
                await RunStage(stage);
            }
            stage++;
            await WalkTo(machine.GlobalPosition.X - 2, machine.GlobalPosition.Z - 2);
        }
        Require(RunContent.Rules!.ObjectiveComplete, "Salvage quota did not unlock extraction."); GD.Print("RUN_OBJECTIVE_PASS salvage=3 extraction_online=true");
        await WalkRoom(Expedition.Layout.Extraction);
        if (!offline)
        {
            Require(!RunContent.Rules.Terminal && Expedition.View!.Inside == 1 && Expedition.View.Active == 2, "First player extracted alone.");
            GD.Print("RUN_FIRST_EXTRACTION_PASS inside=1 active=2 terminal=false");
            await RunStage(520, "run_extract");
        }
        await Until(() => RunContent.Rules!.Phase == RunPhase.Success); if (!offline) await RunStage(521);
        GD.Print("RUN_SUCCESS_PASS all_active=true terminal=Success");
        long oldMax = _world.Objects.Max(o => o.Id.Value); await ResetRun(offline, 530);
        RunContent.ProbeSeed = 654321; StartRound(); CheckFresh(offline ? 1 : 2);
        Require(Expedition.Seed != firstSeed && Expedition.Layout.Signature != firstSignature && Expedition.NetworkObject.Id.Value != oldId && _world.Objects.All(o => o.Id.Value > oldMax) && Machines.SelectMany(m => m.GetServerRewards()).All(p => !rewards.Contains(p.Id)) && RunContent.Rules is { ObjectiveComplete: false, SalvageCount: 0 }, "Fresh round retained state/identities/layout.");
        Require(Expedition.EnvironmentPlan.Signature != firstEnvironmentSignature && Expedition.Environment.StormLevel == 0 && Bodies.All(b => b.Gas.EnvironmentalHeatLoad == 0 && b.Gas.EnvironmentalEnergyDrain == 0 && b.View.Parts.All(p => p.Condition == 1)), "Fresh environment retained state.");
        await Settle(); if (!offline) { await RunStage(531); await EnvironmentStage(607); }
        GD.Print($"RUN_FRESH_PASS seed={Expedition.Seed} signature={Expedition.Layout.Signature} identities=fresh");
        await ResetRun(offline, 532);
        if (!offline) { RpcId(_remote, MethodName.FinishRpc); await Until(() => Multiplayer.GetPeers().Length == 0); }
        GD.Print("RUN_ACCEPTANCE_PASS expedition=true world_empty=true transport_same=true");
    }
    private async Task RunLoopCommand(string command, string samples)
    {
        if (command.StartsWith("run_env_")) { await EnvironmentCommand(command, samples); return; }
        if (command == "run_reset") { await ResetAndCheck(); Require(RunContent.Rules is null && _world.Count == 0, "Client reset retained state."); return; }
        var packet = JsonSerializer.Deserialize<RunPacket>(samples)!;
        await Until(() => _world.Objects.Any(o => o.Host is ExpeditionRun r && r.NetworkObject.Id.Value == packet.Id) && Expedition.View is { } v && v.Phase == packet.View.Phase && v.Salvage == packet.View.Salvage && v.Active == packet.View.Active && v.Inside == packet.View.Inside);
        Require(Expedition.Seed == packet.Seed && Expedition.Layout.Signature == packet.Signature && Expedition.Layout.Rooms.Sum(r => r.Connections.Length) == packet.Connections && Placements() == packet.Placements, "Peers generated different expedition/placements.");
        await Until(() => packet.Threats.All(sample => Parasites.Any(p => p.NetworkObject.Id.Value == sample.Id)));
        foreach (var sample in packet.Threats)
            Require(Expedition.RoomAt(Parasites.Single(p => p.NetworkObject.Id.Value == sample.Id).InitialPosition) == sample.SpawnRoom, "Client parasite placement differs.");
        if (command == "run_chase_motion")
        {
            var sample = packet.Threats.Single(p => p.Id == packet.ParasiteId);
            var threat = Parasites.Single(p => p.NetworkObject.Id.Value == sample.Id);
            await Until(() => threat.State == ParasiteState.Lurking && threat.ChaseTargetId == sample.Target &&
                threat.GlobalPosition.DistanceTo(new Vector3(sample.X, sample.Y, sample.Z)) < 1.5f, 3000);
            GD.Print($"RUN_CLIENT_CHASE_PASS parasite={sample.Id} target={sample.Target} movement_error_under=1.5m");
        }
        bool denied = false; try { Expedition.Publish(packet.View); } catch (InvalidOperationException) { denied = true; }
        Require(denied, "Client changed run state.");
        if (command == "run_team_purge")
        {
            var parasite = Parasites.Single(p => p.NetworkObject.Id.Value == packet.ParasiteId);
            var expected = packet.Threats.Single(p => p.Id == packet.ParasiteId);
            await Until(() => parasite.State == ParasiteState.Attached && parasite.AttachedNetworkObjectId == expected.Attached);
            await WalkRoom(Expedition.RoomAt(parasite.GlobalPosition));
            await WalkTo(parasite.GlobalPosition.X + 1.8f, parasite.GlobalPosition.Z);
            StartPurge(parasite); await Until(() => parasite.State == ParasiteState.Purging); CheckPurgeConfiguration(parasite, true); await SolveLocalPurge(parasite);
        }
        if (command == "run_extract")
        {
            var path = Expedition.Layout.Path(Expedition.RoomAt(LocalBody.GlobalPosition), Expedition.Layout.Extraction);
            foreach (int next in path) { var position = Expedition.Center(next); if (next == Expedition.Layout.Extraction) position += (LocalBody.GlobalPosition - position).Normalized() * 3; await WalkTo(position.X, position.Z); }
        }
        GD.Print($"RUN_CLIENT_OBSERVED seed={Expedition.Seed} signature={Expedition.Layout.Signature} salvage={Expedition.View!.Salvage} phase={Expedition.View.Phase} command={command}");
    }
}
