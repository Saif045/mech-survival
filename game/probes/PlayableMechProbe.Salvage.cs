using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using MechSurvival.Mech;
using MechSurvival.Salvage;

namespace MechSurvival.Probes;

public partial class PlayableMechProbe
{
    private SalvageMachine[] Machines => _world.Objects.Select(o => o.Host).OfType<SalvageMachine>().OrderBy(m => m.NetworkObject.Id.Value).ToArray();
    public sealed record MachineSample(long Id, string State, string Rewards);
    public sealed record SalvagePacket(string Bodies, string Loose, MachineSample[] Machines, long Target, int Symbol, string RewardId);
    private string SalvageCapture(long target = 0, int symbol = 0, string reward = "") => JsonSerializer.Serialize(new SalvagePacket(Capture(), FieldCapture(),
        Machines.Select(m => new MachineSample(m.NetworkObject.Id.Value, m.EncounterState, JsonSerializer.Serialize(m.Rewards))).ToArray(), target, symbol, reward));
    private async Task SalvageStage(int stage, string command = "salvage_observe", SalvageMachine? machine = null, int symbol = 0, PartInstance? reward = null)
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, SalvageCapture(machine?.NetworkObject.Id.Value ?? 0, symbol, reward?.Id.Value.ToString("D") ?? ""));
        await Until(() => _ack == stage);
    }
    private void StartHack(SalvageMachine machine) => LocalBody.GetNode<PlayerInteractor>("PlayerInteractor").RequestInteraction(machine.NetworkObject.Id);
    private async Task HackSuccess(SalvageMachine machine, bool offline, int stage)
    {
        await Until(() => machine.InputOpen);
        int[] sequence = machine.Sequence.ToArray();
        for (int i = 0; i < sequence.Length; i++)
        {
            await Pulse($"hack_{sequence[i]}");
            await Until(() => machine.Progress == i + 1);
            if (!offline) await SalvageStage(stage + i);
        }
        Require(machine.State == SalvageMachineState.Disabled && !machine.HasActiveTimer && machine.OperatorNetworkObjectId == 0,
            "Correct sequence did not disable machine and clear challenge timers/operator.");
    }
    private SalvageMachine SpawnNearLocal(float x = 2.1f)
    {
        var position = LocalBody.GlobalPosition + new Vector3(x, -0.1f, 0);
        return SalvageMachine.Spawn(_world, new Transform3D(Basis.Identity, position));
    }
    private async Task FailureDespawn(SalvageMachine machine, bool offline, int stage, PartInstance[] rewards, string reason)
    {
        NetworkObjectId id = machine.NetworkObject.Id;
        await Until(() => machine.State == SalvageMachineState.SelfDestructing);
        Require(machine.GetNode<Label3D>("Status").Text == "SELF DESTRUCT" && machine.GetServerRewards().Count == 0, "Failure warning/reward loss missing.");
        if (!offline) await SalvageStage(stage);
        await Until(() => !_world.TryGet(id, out _));
        Require(machine.CleanedUp && !machine.HasActiveTimer && rewards.All(r => Loose.All(p => p.Projection.Id != r.Id)), "Failed machine released rewards or retained timers.");
        if (!offline) await SalvageStage(stage + 1);
        GD.Print($"SALVAGE_FAILURE_PASS reason={reason} machine={id} rewards_lost=true timers_cleared=true");
    }
    private async Task SalvageAcceptance(bool offline)
    {
        GD.Print("PLAYABLE_READY role=server");
        if (!offline) await Until(() => _hello);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry(); await Settle();
        foreach (var body in Bodies) body.Gas.SetResourcesForProbe(new(6, 12));
        Require(Machines.Length == 1, "Normal round did not spawn the salvage machine.");
        var machine = Machines[0];
        Require(machine.State == SalvageMachineState.Neutral && machine.OperatorNetworkObjectId == 0 && machine.Sequence.Count == 0 && machine.Progress == 0 &&
            machine.Rewards.SequenceEqual(new[] { new SalvageRewardPreview("industrial_cooling", 0.9f), new SalvageRewardPreview("high_density_power", 0.85f) }), "Neutral machine/preview mismatch.");
        var allRewards = new List<PartInstance>(); var stored = machine.GetServerRewards().ToArray(); allRewards.AddRange(stored);
        if (!offline) await SalvageStage(301);
        // Walk around the Task07 loose leg, using real semantic movement, into encounter range.
        Input.ActionPress("move_left"); await Ticks(8); Input.ActionRelease("move_left");
        Input.ActionPress("move_forward"); await Ticks(12); Input.ActionRelease("move_forward"); await Settle();
        StartHack(machine);
        Require(machine.State == SalvageMachineState.Hacking && machine.OperatorNetworkObjectId == Object(LocalBody).Id.Value, "Host real interaction did not start hack.");
        string before = machine.EncounterState;
        StartHack(machine);
        Require(!machine.TrySubmit(new(1), machine.Sequence[0]) && !machine.TrySubmit(new(1), 99) && before == machine.EncounterState,
            "Preview/invalid/duplicate start changed challenge.");
        if (!offline) await SalvageStage(302, "salvage_attack", machine, machine.Sequence[0]);
        Require(before == machine.EncounterState, "Another peer hijacked/advanced host challenge.");
        await HackSuccess(machine, offline, 303);
        foreach (var reward in stored) Require(ReferenceEquals(reward, LoosePart(reward.Id).GetServerPart()), "Success reconstructed a stored reward instance.");
        Require(machine.GetServerRewards().Count == 0, "Disabled machine retained reward ownership.");
        int count = Loose.Length;
        Require(!machine.TrySubmit(new(1), 1), "Terminal challenge accepted another input.");
        StartHack(machine); await Ticks(3); Require(Loose.Length == count, "Disabled machine yielded duplicate loot.");
        var oldCooling = LocalBody.GetServerPart(MechSlot.Cooling)!;
        var coolingObject = LoosePart(stored[0].Id).NetworkObject.Id;
        await PickupServicePart(stored[0].Id); await SelectServiceSlot(MechSlot.Cooling); await ProbeService();
        await Until(() => !_world.TryGet(coolingObject, out _));
        Require(ReferenceEquals(stored[0], LocalBody.GetServerPart(MechSlot.Cooling)) && ReferenceEquals(oldCooling, LoosePart(oldCooling.Id).GetServerPart()) &&
            Math.Abs(LocalBody.Gas.Snapshot.CoolingCapacity - 7.2f) < 0.0001f && LocalBody.Gas.Snapshot.Energy == 6 && LocalBody.Gas.Snapshot.Heat == 12,
            "Released reward did not use accepted carry/install/GAS path.");
        GD.Print($"SALVAGE_REWARD_PASS host=true cooling_part={stored[0].Id.Value:D} sensor_part={stored[1].Id.Value:D} exact_instances=true carry_install=true");
        if (!offline)
        {
            await SalvageStage(307);
            await SalvageStage(308, "salvage_approach");
            var clientMachine = SalvageMachine.Spawn(_world, new Transform3D(Basis.Identity, new Vector3(-1.6f, 1, -1.2f)));
            var clientRewards = clientMachine.GetServerRewards().ToArray(); allRewards.AddRange(clientRewards);
            await SalvageStage(309, "salvage_start", clientMachine);
            Require(clientMachine.OperatorNetworkObjectId == Object(Bodies[1]).Id.Value, "Client did not own its challenge.");
            before = clientMachine.EncounterState;
            StartHack(clientMachine); // Host is now within actual interaction range, but cannot hijack.
            Require(!clientMachine.TrySubmit(new(1), clientMachine.Sequence[0]) && before == clientMachine.EncounterState, "Host hijacked client challenge.");
            await Until(() => clientMachine.InputOpen);
            int[] sequence = clientMachine.Sequence.ToArray();
            for (int i = 0; i < 4; i++)
            {
                await SalvageStage(310 + i, "salvage_input", clientMachine, sequence[i]);
                Require(clientMachine.Progress == i + 1, "Server did not observe client challenge progress.");
            }
            Require(clientMachine.State == SalvageMachineState.Disabled && clientRewards.All(r => ReferenceEquals(r, LoosePart(r.Id).GetServerPart())), "Client success lost reward references.");
            var sensor = Bodies[1].GetServerPart(MechSlot.Power)!;
            var incoming = LoosePart(clientRewards[1].Id).NetworkObject.Id;
            await SalvageStage(314, "salvage_install", clientMachine, reward: clientRewards[1]);
            await Until(() => !_world.TryGet(incoming, out _));
            Require(ReferenceEquals(clientRewards[1], Bodies[1].GetServerPart(MechSlot.Power)) && ReferenceEquals(sensor, LoosePart(sensor.Id).GetServerPart()), "Client reward carry/install lost identity.");
            before = clientMachine.EncounterState; count = Loose.Length;
            await SalvageStage(315, "salvage_repeat", clientMachine);
            Require(before == clientMachine.EncounterState && count == Loose.Length, "Client duplicated terminal rewards.");
            await SalvageStage(316);
            GD.Print($"SALVAGE_REWARD_PASS client=true sensor_part={clientRewards[1].Id.Value:D} exact_instance=true carry_install=true");
        }
        // Wrong symbol, timeout, and moving out of range each lose unreleased rewards.
        var wrong = SpawnNearLocal(); var lost = wrong.GetServerRewards().ToArray(); allRewards.AddRange(lost);
        StartHack(wrong); await Until(() => wrong.InputOpen);
        await Pulse($"hack_{wrong.Sequence[0] % 4 + 1}");
        await FailureDespawn(wrong, offline, 317, lost, "wrong_symbol");
        var timeout = SpawnNearLocal(); lost = timeout.GetServerRewards().ToArray(); allRewards.AddRange(lost);
        StartHack(timeout);
        await FailureDespawn(timeout, offline, 319, lost, "timeout");
        var range = SpawnNearLocal(); lost = range.GetServerRewards().ToArray(); allRewards.AddRange(lost);
        StartHack(range);
        Input.ActionPress("move_backward"); await Ticks(24); Input.ActionRelease("move_backward"); await Settle();
        await FailureDespawn(range, offline, 321, lost, "range");
        // Leave all live encounter states for real lifecycle teardown.
        var warning = SpawnNearLocal(); allRewards.AddRange(warning.GetServerRewards());
        StartHack(warning); await Until(() => warning.InputOpen); await Pulse($"hack_{warning.Sequence[0] % 4 + 1}");
        var active = SpawnNearLocal(-2.1f); allRewards.AddRange(active.GetServerRewards()); StartHack(active);
        var neutral = SalvageMachine.Spawn(_world, new Transform3D(Basis.Identity, new Vector3(10, 1, 10))); allRewards.AddRange(neutral.GetServerRewards());
        Require(Machines.Any(m => m.State == SalvageMachineState.Disabled) && active.State == SalvageMachineState.Hacking &&
            warning.State == SalvageMachineState.SelfDestructing && neutral.State == SalvageMachineState.Neutral, "All-state cleanup setup failed.");
        if (!offline) await SalvageStage(323);
        var oldMachines = Machines.ToArray(); var oldMax = _world.Objects.Max(o => o.Id.Value);
        var oldIds = allRewards.Select(r => r.Id).ToArray();
        if (!offline) RpcId(_remote, MethodName.CommandRpc, 324, "salvage_reset", "");
        await ResetAndCheck();
        Require(Machines.Length == 0 && Loose.Length == 0 && oldMachines.All(m => m.CleanedUp && !m.HasActiveTimer && m.OperatorNetworkObjectId == 0), "Round retained encounter state/timers/loot.");
        if (!offline) await Until(() => _ack == 324);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry();
        var fresh = Machines.Single();
        Require(_world.Objects.All(o => o.Id.Value > oldMax) && fresh.State == SalvageMachineState.Neutral && fresh.Progress == 0 &&
            fresh.OperatorNetworkObjectId == 0 && fresh.Sequence.Count == 0 && !fresh.HasActiveTimer &&
            fresh.GetServerRewards().All(r => !oldIds.Contains(r.Id) && allRewards.All(old => !ReferenceEquals(r, old))), "Fresh round reused machine/rewards or challenge state.");
        await Settle();
        if (!offline) await SalvageStage(325, "salvage_fresh");
        if (!offline) RpcId(_remote, MethodName.CommandRpc, 326, "salvage_reset", "");
        await ResetAndCheck();
        if (!offline)
        {
            await Until(() => _ack == 326); RpcId(_remote, MethodName.FinishRpc); await Until(() => Multiplayer.GetPeers().Length == 0);
        }
        GD.Print("SALVAGE_ACCEPTANCE_PASS exact_rewards=true carry_install=true duplicate_rejected=true wrong_timeout_range=true all_state_cleanup=true fresh_round=true");
    }
    private async Task SalvageCommand(string command, string samples)
    {
        if (command == "salvage_reset")
        {
            var old = Machines.ToArray(); RememberRound(); await ResetAndCheck();
            Require(Machines.Length == 0 && Loose.Length == 0 && old.All(m => m.CleanedUp && !m.HasActiveTimer && m.OperatorNetworkObjectId == 0), "Client retained encounter timers/state."); return;
        }
        var packet = JsonSerializer.Deserialize<SalvagePacket>(samples)!;
        var bodies = JsonSerializer.Deserialize<Sample[]>(packet.Bodies)!;
        var parts = JsonSerializer.Deserialize<FieldSample[]>(packet.Loose)!;
        await Until(() => Matches(bodies, false) && Machines.Length == packet.Machines.Length && packet.Machines.All(s => Machines.Any(m =>
            m.NetworkObject.Id.Value == s.Id && m.EncounterState == s.State && JsonSerializer.Serialize(m.Rewards) == s.Rewards)) &&
            Loose.Length == parts.Length && parts.All(s => Loose.Any(p => p.NetworkObject.Id.Value == s.ObjectId &&
            p.Projection.Id.Value.ToString("D") == s.PartId && p.Projection.Model == s.Model && p.Projection.Condition == s.Condition &&
            p.Carry.HolderNetworkObjectId == s.Holder && p.Carry.WorldTransform.Origin.DistanceTo(new(s.X, s.Y, s.Z)) < 0.02f)));
        foreach (var body in Bodies) CheckBody(body);
        foreach (var machine in Machines)
        {
            bool denied = false;
            try { machine.GetServerRewards(); } catch (InvalidOperationException) { denied = true; }
            Require(denied, "Client accessed mutable rewards.");
        }
        var target = Machines.FirstOrDefault(m => m.NetworkObject.Id.Value == packet.Target);
        switch (command)
        {
            case "salvage_attack":
                LocalBody.GetNode<PlayerInteractor>("PlayerInteractor").RequestInteraction(target!.NetworkObject.Id);
                target.RequestHackInput(packet.Symbol);
                target.RpcId(1, "SubmitHackRpc", packet.Symbol); // Deliberately bypass owner-side gate.
                target.RpcId(1, "SubmitHackRpc", 99);
                bool denied = false;
                try { target.TrySubmit(new(1), packet.Symbol); } catch (InvalidOperationException) { denied = true; }
                Require(denied, "Client mutated authoritative challenge.");
                await Ticks(3); break;
            case "salvage_approach":
                Input.ActionPress("move_left"); await Ticks(8); Input.ActionRelease("move_left"); await Settle(); break;
            case "salvage_start":
                StartHack(target!); await Until(() => target!.State == SalvageMachineState.Hacking && target.OperatorNetworkObjectId == Object(LocalBody).Id.Value); break;
            case "salvage_input":
                await Until(() => target!.InputOpen);
                int progress = target!.Progress;
                await Pulse($"hack_{packet.Symbol}");
                await Until(() => target.Progress == progress + 1); break;
            case "salvage_install":
                var part = new MechPartId(Guid.Parse(packet.RewardId)); var obj = LoosePart(part).NetworkObject.Id;
                await PickupServicePart(part); await SelectServiceSlot(MechSlot.Power); await ProbeService();
                await Until(() => LocalBody.GetPart(MechSlot.Power)?.Id == part && !_world.TryGet(obj, out _));
                Require(!LocalBody.GetNode<PlayerCarrier>("PlayerCarrier").HasCarriedItem && LocalBody.GetPart(MechSlot.Power)!.Condition == 0.85f, "Client reward install/carry reconciliation failed."); break;
            case "salvage_repeat":
                StartHack(target!); target!.RpcId(1, "SubmitHackRpc", 1); await Ticks(3); break;
            case "salvage_fresh":
                CheckFresh(2);
                Require(Machines.Length == 1 && Machines[0].State == SalvageMachineState.Neutral && Machines[0].Progress == 0 &&
                    Machines[0].OperatorNetworkObjectId == 0 && Machines[0].Sequence.Count == 0 && _world.Objects.All(o => o.Id.Value > _oldMax), "Client fresh encounter state invalid.");
                GD.Print("SALVAGE_CLIENT_PASS host_client_success=true progress_loot_converged=true forged_rejected=true failures_observed=true carry_install=true fresh_round=true"); break;
        }
        GD.Print($"SALVAGE_OBSERVED command={command} machines={Machines.Length} loose={Loose.Length} canonical_matched=true");
    }
}
