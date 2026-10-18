using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using MechSurvival.Mech.Gas;
using MechSurvival.Parasites;
using MechSurvival.Player;
using MechSurvival.Salvage;

namespace MechSurvival.Probes;

public partial class PlayableMechProbe
{
    private Parasite[] Parasites => _world.Objects.Select(o => o.Host).OfType<Parasite>().OrderBy(p => p.NetworkObject.Id.Value).ToArray();
    public sealed record ParasiteSample(long Id, string State);
    public sealed record ParasitePacket(string Bodies, ParasiteSample[] Parasites, long Target, bool Team);
    private string ParasiteCapture(long target = 0, bool team = false) => JsonSerializer.Serialize(new ParasitePacket(Capture(),
        Parasites.Select(p => new ParasiteSample(p.NetworkObject.Id.Value, p.InfectionState)).ToArray(), target, team));
    private async Task ParasiteStage(int stage, string command = "parasite_observe", Parasite? target = null, bool team = false)
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, ParasiteCapture(target?.NetworkObject.Id.Value ?? 0, team));
        await Until(() => _ack == stage);
    }
    private static void CheckParasiteGas(PlayableMech body)
    {
        Require(body.Gas.UsesRealAsc && body.Gas.ReadSnapshot() == body.Gas.Snapshot && body.GasState == body.Gas.Snapshot.Serialize(), "Parasite resource state bypassed native GAS.");
        Require(body.Gas.Snapshot.PowerDepleted == (body.Gas.Snapshot.Energy <= 0) &&
            body.Gas.Snapshot.ThermalCritical == (body.Gas.Snapshot.Heat >= 100), "Native resource tags disagree with resources.");
    }
    private void CheckPressure(PlayableMech body, Parasite parasite)
    {
        Require(body.Gas.ParasiteSourceId == parasite.NetworkObject.Id.Value && body.Gas.ParasiteEnergyDrain == Parasite.EnergyDrain &&
            body.Gas.ParasiteHeatLoad == Parasite.HeatLoad && body.Gas.ExternalHeatLoad == 1, "Parasite load overwrote another input or was not applied.");
        var before = body.Gas.Snapshot;
        var result = body.Gas.AdvanceResources(1);
        Require(result.ExternalEnergySpent > 0 && body.Gas.Snapshot.Energy < before.Energy && body.Gas.Snapshot.Heat > before.Heat,
            "Parasite did not worsen canonical Energy/Heat.");
        Require(body.Gas.Snapshot.Mobility == before.Mobility && body.Gas.Snapshot.PowerCapacity == before.PowerCapacity && body.Gas.Snapshot.CoolingCapacity == before.CoolingCapacity,
            "Resource pressure changed structural attributes.");
        CheckParasiteGas(body);
        GD.Print($"PARASITE_PRESSURE_PASS body={Object(body).Id} energy={before.Energy}->{body.Gas.Snapshot.Energy} heat={before.Heat}->{body.Gas.Snapshot.Heat} external_spent={result.ExternalEnergySpent} native=true");
    }
    private void StartPurge(Parasite parasite) => LocalBody.GetNode<PlayerInteractor>("PlayerInteractor").RequestInteraction(parasite.NetworkObject.Id);
    private static void CheckPurgeConfiguration(Parasite parasite, bool team)
    {
        Require(parasite.State == ParasiteState.Purging && parasite.Teammate == team && parasite.RequiredHits == (team ? 2 : 3) &&
            parasite.SafeStart == (team ? 2 : 3) && parasite.SafeEnd == (team ? 5 : 4), "Self/teammate purge advantage missing.");
    }
    private async Task SolveLocalPurge(Parasite parasite)
    {
        var id = parasite.NetworkObject.Id;
        Require(_world.TryGet(new(parasite.AttachedNetworkObjectId), out var infected), "Missing infected body.");
        var body = (PlayableMech)infected!.Host;
        int required = parasite.RequiredHits;
        for (int hit = 0; hit < required; hit++)
        {
            await Until(() => parasite.State == ParasiteState.Purging && parasite.Armed && parasite.Cursor == 3);
            var resources = body.Gas.Snapshot;
            await Pulse("purge_pulse");
            if (hit == required - 1)
            {
                await Until(() => !_world.TryGet(id, out _));
                Require(body.Gas.Snapshot == resources && parasite.CleanedUp && !parasite.HasActiveTimer, "Purge restored resources or retained timers.");
            }
            else
            {
                await Until(() => parasite.Progress == hit + 1);
                parasite.RequestPulse(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(parasite.Progress == hit + 1, "Repeated safe-window pulse scored twice.");
            }
            GD.Print($"PARASITE_TIMING_HIT owner={Multiplayer.GetUniqueId()} hit={hit + 1}/{required} anti_mash=true");
        }
    }
    private static void CheckCleared(PlayableMech body, MechGasSnapshot resources)
    {
        Require(body.Gas.ParasiteSourceId == 0 && body.Gas.ParasiteEnergyDrain == 0 && body.Gas.ParasiteHeatLoad == 0 &&
            body.Gas.ExternalHeatLoad == 1 && body.Gas.Snapshot == resources, "Removal cleared unrelated inputs or restored resources.");
        CheckParasiteGas(body);
    }
    private async Task<Parasite> Infect(PlayableMech body)
    {
        var parasite = Parasite.Spawn(_world, new Transform3D(Basis.Identity, body.GlobalPosition + new Vector3(0.2f, 0, 0)));
        await Until(() => parasite.State == ParasiteState.Attached && parasite.AttachedNetworkObjectId == Object(body).Id.Value);
        await Until(() => parasite.GlobalPosition.DistanceTo(body.GetNode<Node3D>("Presentation/ParasiteAnchor").GlobalPosition) < 0.02f);
        return parasite;
    }
    private async Task ParasiteReset(int stage, bool offline)
    {
        var oldParasites = Parasites.ToArray(); var oldGas = Bodies.Select(b => b.Gas).ToArray();
        if (!offline) RpcId(_remote, MethodName.CommandRpc, stage, "parasite_reset", "");
        await ResetAndCheck();
        Require(Parasites.Length == 0 && oldParasites.All(p => p.CleanedUp && !p.HasActiveTimer && p.AttachedNetworkObjectId == 0 && p.OperatorNetworkObjectId == 0) &&
            oldGas.All(g => g.ParasiteSourceId == 0 && g.ParasiteEnergyDrain == 0 && g.ParasiteHeatLoad == 0), "Round retained parasite state/load/timers.");
        if (!offline) await Until(() => _ack == stage);
    }
    private async Task ParasiteAcceptance(bool offline)
    {
        // Begin before the observing client connects: real existing-spawn attached-state replication.
        StartRound(); CheckFresh(1); CheckRegistry(); await Settle();
        Require(Machines.Single().State == SalvageMachineState.Neutral && Loose.Length == 2 && Parasites.Single().State == ParasiteState.Lurking,
            "Normal parasite/salvage/field composition missing.");
        var parasite = Parasites.Single(); var identity = parasite.NetworkObject.Id; var parent = parasite.GetParent();
        Require(!parasite.TryPulse(new(1)), "Unattached parasite accepted purge.");
        Input.ActionPress("move_left"); await Ticks(8); Input.ActionRelease("move_left");
        Input.ActionPress("move_backward"); await Ticks(12); Input.ActionRelease("move_backward"); await Settle();
        await Until(() => parasite.State == ParasiteState.Attached);
        var host = LocalBody;
        Require(parasite.NetworkObject.Id == identity && parasite.GetParent() == parent && parasite.NetworkObject.OwnerPeerId.Value == 1 &&
            parasite.GetMultiplayerAuthority() == 1, "Attachment changed parasite identity/parent/authority.");
        host.Gas.SetExternalHeatLoad(1); host.Gas.SetResourcesForProbe(new(8, 10)); host.Gas.SetDevelopmentModifier(true);
        CheckPressure(host, parasite);
        var automatic = host.Gas.Snapshot;
        host.Gas.SetResourceSimulationEnabled(true);
        await Until(() => host.Gas.Snapshot.Energy < automatic.Energy && host.Gas.Snapshot.Heat > automatic.Heat);
        host.Gas.SetResourceSimulationEnabled(false);
        Vector3 beforeMove = host.Position;
        Input.ActionPress("move_right"); await Ticks(4); Input.ActionRelease("move_right"); await Settle();
        await Until(() => parasite.GlobalPosition.DistanceTo(host.GetNode<Node3D>("Presentation/ParasiteAnchor").GlobalPosition) < 0.02f);
        Require(host.Position.DistanceTo(beforeMove) > 0.2f && parasite.NetworkObject.Id == identity, "Attached presentation did not follow actual motion.");
        // The source-owned slot enforces concrete one-parasite capacity; stale cleanup cannot steal it.
        var blocked = Parasite.Spawn(_world, new Transform3D(Basis.Identity, host.GlobalPosition)); var blockedId = blocked.NetworkObject.Id;
        await Ticks(6);
        Require(blocked.State == ParasiteState.Lurking && !host.Gas.TrySetParasiteLoad(blockedId, 0.2f, 6), "Second parasite infected the same mech.");
        host.Gas.ClearParasiteLoad(blockedId); Require(host.Gas.ParasiteSourceId == identity.Value, "Stale cleanup cleared another parasite's load.");
        _world.Despawn(blockedId); await Until(() => !_world.TryGet(blockedId, out _));
        GD.Print("PLAYABLE_READY role=server");
        if (!offline) { await Until(() => _hello); await ParasiteStage(401); }
        StartPurge(parasite); CheckPurgeConfiguration(parasite, false);
        var resources = host.Gas.Snapshot;
        await SolveLocalPurge(parasite); CheckCleared(host, resources);
        Require(host.Gas.HasDevelopmentModifier && host.GasMobility == 2.5f, "Removal reset unrelated native effect or structural baseline.");
        if (!offline) await ParasiteStage(402);
        // Reset an attached source as well as a lurking object, not only successful removals.
        await Infect(host);
        Parasite.Spawn(_world, new Transform3D(Basis.Identity, new Vector3(15, 1, 15)));
        if (!offline) await ParasiteStage(403);
        await ParasiteReset(404, offline);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry(); await Settle();
        foreach (var body in Bodies) { body.Gas.SetExternalHeatLoad(1); body.Gas.SetResourcesForProbe(new(8, 10)); }
        host = Bodies[0];
        // Small carry regression: the existing loose field leg remains usable.
        var leg = Loose.Single(p => p.Projection.Model == "debug_leg");
        await PickupServicePart(leg.Projection.Id); host.GetNode<PlayerCarrier>("PlayerCarrier").RequestDrop();
        await Until(() => leg.Carry.HolderNetworkObjectId == 0);
        Require(Machines.Single().State == SalvageMachineState.Neutral && host.View.Parts.Count == 9, "Normal encounter/body composition changed.");
        if (offline)
        {
            parasite = await Infect(host); StartPurge(parasite);
            Require(!parasite.TryPulse(new(1)) && parasite.Progress == 0, "Outside-window pulse scored.");
            host.Gas.SetResourceSimulationEnabled(true);
            await Until(() => parasite.State == ParasiteState.Attached, 15000);
            host.Gas.SetResourceSimulationEnabled(false);
            Require(parasite.OperatorNetworkObjectId == 0 && parasite.Progress == 0 && !parasite.HasActiveTimer &&
                host.Gas.ParasiteSourceId == parasite.NetworkObject.Id.Value && host.Gas.Snapshot.Energy < 8 && host.Gas.Snapshot.Heat > 10,
                "Timeout did not preserve infection/pressure and clear operator/progress.");
            CheckParasiteGas(host); resources = host.Gas.Snapshot;
            StartPurge(parasite); await SolveLocalPurge(parasite); CheckCleared(host, resources);
            GD.Print("PARASITE_RETRY_PASS timeout=true pressure_continued=true retry=true no_resource_restore=true");
        }
        else
        {
            await ParasiteStage(405);
            await ParasiteStage(406, "parasite_approach");
            parasite = await Infect(host); CheckPressure(host, parasite);
            await ParasiteStage(407);
            await ParasiteStage(408, "parasite_start", parasite, true); CheckPurgeConfiguration(parasite, true);
            int progress = parasite.Progress; long owner = parasite.OperatorNetworkObjectId;
            StartPurge(parasite); Require(!parasite.TryPulse(new(1)) && parasite.Progress == progress && parasite.OperatorNetworkObjectId == owner, "Host hijacked client teammate purge.");
            resources = host.Gas.Snapshot;
            await ParasiteStage(410, "parasite_solve", parasite, true); CheckCleared(host, resources);
            await ParasiteStage(411);
            var client = Bodies[1];
            parasite = await Infect(client); CheckPressure(client, parasite); await ParasiteStage(412);
            StartPurge(parasite); CheckPurgeConfiguration(parasite, true);
            await ParasiteStage(414, "parasite_attack", parasite, true);
            Require(parasite.Progress == 0 && parasite.OperatorNetworkObjectId == Object(host).Id.Value, "Client hijacked host teammate purge.");
            resources = client.Gas.Snapshot;
            await SolveLocalPurge(parasite); CheckCleared(client, resources); await ParasiteStage(415);
            parasite = await Infect(client); CheckPressure(client, parasite);
            await ParasiteStage(416);
            await ParasiteStage(417, "parasite_start", parasite, false); CheckPurgeConfiguration(parasite, false);
            resources = client.Gas.Snapshot;
            await ParasiteStage(418, "parasite_solve", parasite, false); CheckCleared(client, resources);
            await ParasiteStage(419);
            GD.Print("PARASITE_COOP_PASS host_infected_client_purge=true client_infected_host_purge=true client_self=true no_restore=true");
        }
        parasite = await Infect(host); StartPurge(parasite);
        Require(Parasites.Any(p => p.State == ParasiteState.Lurking) && parasite.State == ParasiteState.Purging, "Purge/lurking cleanup setup failed.");
        if (!offline) await ParasiteStage(420);
        long maximum = _world.Objects.Max(o => o.Id.Value);
        await ParasiteReset(421, offline);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry(); await Settle();
        Require(Parasites.Length == 1 && Parasites[0].State == ParasiteState.Lurking && Parasites[0].AttachedNetworkObjectId == 0 &&
            Parasites[0].OperatorNetworkObjectId == 0 && !Parasites[0].HasActiveTimer && _world.Objects.All(o => o.Id.Value > maximum) &&
            Bodies.All(b => b.Gas.ParasiteSourceId == 0 && b.Gas.ParasiteEnergyDrain == 0 && b.Gas.ParasiteHeatLoad == 0), "Fresh round retained parasite state/identity/load.");
        if (!offline) await ParasiteStage(422, "parasite_fresh");
        await ParasiteReset(423, offline);
        if (!offline) { RpcId(_remote, MethodName.FinishRpc); await Until(() => Multiplayer.GetPeers().Length == 0); }
        GD.Print($"PARASITE_ACCEPTANCE_PASS automatic_attachment=true stable_identity=true canonical_pressure=true self=true teammate={!offline} timeout_retry={offline} no_restore=true late_observation={!offline} cleanup_rounds=3");
    }
    private async Task ParasiteCommand(string command, string samples)
    {
        if (command == "parasite_reset")
        {
            var old = Parasites.ToArray(); RememberRound(); await ResetAndCheck();
            Require(Parasites.Length == 0 && old.All(p => p.CleanedUp && !p.HasActiveTimer && p.AttachedNetworkObjectId == 0 && p.OperatorNetworkObjectId == 0), "Client retained parasite/timer state."); return;
        }
        var packet = JsonSerializer.Deserialize<ParasitePacket>(samples)!; var bodies = JsonSerializer.Deserialize<Sample[]>(packet.Bodies)!;
        await Until(() => Matches(bodies, false) && Parasites.Length == packet.Parasites.Length && packet.Parasites.All(s =>
            Parasites.Any(p => p.NetworkObject.Id.Value == s.Id && p.InfectionState == s.State)));
        foreach (var body in Bodies)
        {
            CheckBody(body); CheckParasiteGas(body);
            Action[] attempts = [() => body.Gas.TrySetParasiteLoad(new(999999), 1, 1), () => body.Gas.ClearParasiteLoad(new(999999)), () => body.Gas.SetExternalHeatLoad(100)];
            foreach (var attempt in attempts)
            {
                bool denied = false; try { attempt(); } catch (InvalidOperationException) { denied = true; }
                Require(denied, "Client changed parasite resource inputs.");
            }
        }
        foreach (var parasite in Parasites)
        {
            Require(parasite.NetworkObject.OwnerPeerId.Value == 1 && parasite.GetMultiplayerAuthority() == 1 && parasite.GetParent().GetParent() == _world, "Parasite was reparented or authority transferred.");
            if (parasite.AttachedNetworkObjectId > 0)
                await Until(() => _world.TryGet(new(parasite.AttachedNetworkObjectId), out var body) &&
                    parasite.GlobalPosition.DistanceTo(body!.Host.GetNode<Node3D>("Presentation/ParasiteAnchor").GlobalPosition) < 0.03f);
        }
        var target = Parasites.FirstOrDefault(p => p.NetworkObject.Id.Value == packet.Target);
        switch (command)
        {
            case "parasite_approach": Input.ActionPress("move_left"); await Ticks(8); Input.ActionRelease("move_left"); await Settle(); break;
            case "parasite_start":
                StartPurge(target!); await Until(() => target!.State == ParasiteState.Purging && target.OperatorNetworkObjectId == Object(LocalBody).Id.Value);
                CheckPurgeConfiguration(target!, packet.Team); break;
            case "parasite_solve": CheckPurgeConfiguration(target!, packet.Team); await SolveLocalPurge(target!); break;
            case "parasite_attack":
                StartPurge(target!); target!.RequestPulse(); target.RpcId(1, "PulseRpc");
                bool denied = false; try { target.TryPulse(new(1)); } catch (InvalidOperationException) { denied = true; }
                Require(denied, "Client executed canonical purge mutation."); await Ticks(3); break;
            case "parasite_fresh":
                CheckFresh(2); Require(Parasites.Length == 1 && Parasites[0].State == ParasiteState.Lurking && _world.Objects.All(o => o.Id.Value > _oldMax), "Client fresh parasite identity/state invalid.");
                GD.Print("PARASITE_CLIENT_PASS late_attached=true both_coop_directions=true client_self=true forged_load_guards=true native_gas=true fresh_round=true"); break;
        }
        GD.Print($"PARASITE_OBSERVED command={command} parasites={Parasites.Length} canonical_body_gas_state=true anchor=true");
    }
}
