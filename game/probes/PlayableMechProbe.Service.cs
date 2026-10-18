using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using MechSurvival.Mech;
using MechSurvival.Player;

namespace MechSurvival.Probes;

public partial class PlayableMechProbe
{
    public sealed record ServiceState(string Bodies, string Loose);
    private MechPartId _clientServicePart;
    private string[] _serviceOldIds = [];
    private PlayableMech LocalBody => Bodies.Single(b => b.GetNode("Input").IsMultiplayerAuthority());
    private static MechServiceController Service(PlayableMech body) => body.GetNode<MechServiceController>("MechServiceController");
    private LooseMechPart LoosePart(MechPartId id) => Loose.Single(p => p.Projection.Id == id);
    private async Task Pulse(string action)
    {
        Input.ActionPress(action);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ActionRelease(action);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task SelectServiceSlot(MechSlot slot)
    {
        var controller = Service(LocalBody);
        for (int i = 0; i < 9 && controller.SelectedSlot != slot; i++) await Pulse("service_next_slot");
        Require(controller.SelectedSlot == slot, "Semantic slot selection failed.");
        Require(controller.GetNode<Label>("HUD/SlotLabel").Visible &&
            controller.GetNode<Label>("HUD/SlotLabel").Text.Contains(slot.ToString()), "Owner selection feedback missing.");
    }
    private async Task PickupServicePart(MechPartId id)
    {
        var part = LoosePart(id);
        LocalBody.GetNode<PlayerInteractor>("PlayerInteractor").RequestInteraction(part.NetworkObject.Id);
        await Until(() => part.Carry.HolderNetworkObjectId == Object(LocalBody).Id.Value);
        await Until(() => part.GlobalPosition.DistanceTo(LocalBody.GetNode<PlayerCarrier>("PlayerCarrier").CarryAnchor.GlobalPosition) < 0.02f);
    }
    private static void ServiceGas(PlayableMech body, float mobility)
    {
        Require(Math.Abs(body.GasMobility - mobility) < 0.0001f && body.GasHasMobility &&
            Math.Abs(body.GasMoveSpeed - mobility * 3) < 0.0001f, "Service did not change canonical Mobility/speed.");
        Require(body.Gas.ReadSnapshot() == body.Gas.Snapshot && body.Gas.Snapshot.Serialize() == body.GasState && body.Gas.UsesRealAsc,
            "Service bypassed native GAS.");
        Require(body.Gas.Snapshot.Energy == 6 && body.Gas.Snapshot.Heat == 12, "Leg service reset Energy/Heat.");
    }
    private async Task ServiceMeasure(string label, float mobility)
    {
        ServiceGas(LocalBody, mobility);
        await MeasureOwner("service_" + label, "move_left");
        ServiceGas(LocalBody, mobility);
        // Return to the nearby service parts using the same accepted semantic movement path.
        await DriveOwner("move_right"); await Settle();
    }
    private async Task ServiceStage(int stage, string command = "service_observe")
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, JsonSerializer.Serialize(new ServiceState(Capture(), FieldCapture())));
        await Until(() => _ack == stage);
    }
    private async Task ServiceAcceptance(bool offline)
    {
        GD.Print("PLAYABLE_READY role=server");
        if (!offline) await Until(() => _hello);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry();
        Require(Loose.Length == 2, "Task07 production content missing.");
        foreach (var body in Bodies) body.Gas.SetResourcesForProbe(new(6, 12));
        await Settle();
        var host = Bodies[0]; var controller = Service(host);
        var original = host.GetServerPart(MechSlot.LeftLeg)!;
        var field = Loose.Single(p => p.Projection.Model == "debug_leg");
        var fieldInstance = field.GetServerPart(); var fieldObject = field.NetworkObject.Id;
        if (!offline) await ServiceStage(201);
        await ServiceMeasure("pristine", 2);
        await SelectServiceSlot(MechSlot.LeftLeg);
        await Pulse("service_part");
        Require(!host.HasPart(MechSlot.LeftLeg), "Host semantic service did not detach leg.");
        var detached = LoosePart(original.Id); var detachedObject = detached.NetworkObject.Id;
        Require(ReferenceEquals(original, detached.GetServerPart()) && detached.Projection.Condition == 1, "Detach reconstructed the part.");
        ServiceGas(host, 1);
        string unchanged = host.Composition + host.GasState + FieldCapture();
        Require(!controller.TryService(new(1), MechSlot.LeftLeg) && !controller.TryService(new(1), (MechSlot)99), "Empty/invalid slot accepted.");
        Require(unchanged == host.Composition + host.GasState + FieldCapture(), "Rejected service changed state.");
        if (!offline) await ServiceStage(202);
        await ServiceMeasure("detached", 1);
        await PickupServicePart(original.Id);
        await Pulse("service_part");
        await Until(() => !_world.TryGet(detachedObject, out _));
        Require(ReferenceEquals(original, host.GetServerPart(MechSlot.LeftLeg)) && !host.GetNode<PlayerCarrier>("PlayerCarrier").HasCarriedItem,
            "Reinstall lost instance or carrier capacity.");
        ServiceGas(host, 2);
        if (!offline) await ServiceStage(203);
        await ServiceMeasure("reinstalled", 2);
        await PickupServicePart(fieldInstance.Id);
        await Pulse("service_part");
        await Until(() => !_world.TryGet(fieldObject, out _));
        Require(ReferenceEquals(fieldInstance, host.GetServerPart(MechSlot.LeftLeg)) &&
            ReferenceEquals(original, LoosePart(original.Id).GetServerPart()) && LoosePart(original.Id).NetworkObject.Id != detachedObject,
            "Replacement did not transfer the exact incoming/outgoing instances.");
        Require(!host.GetNode<PlayerCarrier>("PlayerCarrier").HasCarriedItem, "Installed incoming part retained carry ownership.");
        ServiceGas(host, 1.8f);
        if (!offline) await ServiceStage(204);
        await ServiceMeasure("replaced", 1.8f);
        // The displaced full leg is a real interactable; an incompatible request must leave it carried.
        await PickupServicePart(original.Id);
        await SelectServiceSlot(MechSlot.Sensor);
        unchanged = host.Composition + host.GasState + FieldCapture();
        await Pulse("service_part"); await Ticks(3);
        Require(unchanged == host.Composition + host.GasState + FieldCapture() && host.GetNode<PlayerCarrier>("PlayerCarrier").HasCarriedItem,
            "Incompatible service mutated body/carry/world state.");
        host.GetNode<PlayerCarrier>("PlayerCarrier").RequestDrop();
        await Until(() => LoosePart(original.Id).Carry.HolderNetworkObjectId == 0);
        if (!offline)
        {
            await ServiceStage(205);
            var client = Bodies[1]; var clientOriginal = client.GetServerPart(MechSlot.LeftLeg)!;
            await ServiceStage(206, "service_detach");
            Require(!client.HasPart(MechSlot.LeftLeg) && ReferenceEquals(clientOriginal, LoosePart(clientOriginal.Id).GetServerPart()), "Client detach lost exact instance.");
            ServiceGas(client, 1);
            var clientDetached = LoosePart(clientOriginal.Id).NetworkObject.Id;
            await ServiceStage(207);
            Vector3 before = client.Position;
            await ServiceStage(208, "service_move");
            await Settle();
            CheckDisplacement(client, before, 3, "service_client_detached", "server_client");
            await ServiceStage(209, "service_return");
            await ServiceStage(210, "service_reinstall");
            await Until(() => !_world.TryGet(clientDetached, out _));
            Require(ReferenceEquals(clientOriginal, client.GetServerPart(MechSlot.LeftLeg)), "Client reinstall reconstructed part.");
            ServiceGas(client, 2);
            await ServiceStage(211);
            // Approach the host's displaced leg without changing server transforms or carry state.
            await ServiceStage(212, "service_approach");
            await ServiceStage(213, "service_replace");
            Require(ReferenceEquals(original, client.GetServerPart(MechSlot.LeftLeg)) &&
                ReferenceEquals(clientOriginal, LoosePart(clientOriginal.Id).GetServerPart()), "Client replacement lost incoming/outgoing references.");
            ServiceGas(client, 2);
            await ServiceStage(214);
            unchanged = Capture() + FieldCapture();
            await ServiceStage(215, "service_attack");
            await Ticks(6);
            Require(unchanged == Capture() + FieldCapture(), "Forged owner or invalid-slot service changed authoritative state.");
            await ServiceStage(216);
        }
        _serviceOldIds = Bodies.SelectMany(b => b.View.Parts.Select(p => p.Id.ToString())).Concat(Loose.Select(p => p.Projection.Id.ToString())).ToArray();
        RememberRound();
        if (!offline) RpcId(_remote, MethodName.CommandRpc, 217, "service_reset", "");
        await ResetAndCheck(); Require(Loose.Length == 0, "Service loose embodiments survived reset.");
        if (!offline) await Until(() => _ack == 217);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry();
        Require(_world.Objects.All(o => o.Id.Value > _oldMax) &&
            Bodies.SelectMany(b => b.View.Parts.Select(p => p.Id.ToString())).Concat(Loose.Select(p => p.Projection.Id.ToString())).All(id => !_serviceOldIds.Contains(id)),
            "Service round reused identities.");
        await Settle();
        if (!offline) await ServiceStage(218, "service_fresh");
        if (!offline) RpcId(_remote, MethodName.CommandRpc, 219, "service_reset", "");
        await ResetAndCheck();
        if (!offline)
        {
            await Until(() => _ack == 219);
            RpcId(_remote, MethodName.FinishRpc);
            await Until(() => Multiplayer.GetPeers().Length == 0);
        }
        GD.Print("SERVICE_ACCEPTANCE_PASS exact_instances=true stable_part_ids=true mobility=2,1,2,1.8 speed=6,3,6,5.4 energy_heat_preserved=true rejected_unchanged=true rounds=2");
    }
    private async Task ServiceCommand(string command, string samples)
    {
        if (command == "service_reset")
        {
            _serviceOldIds = Bodies.SelectMany(b => b.View.Parts.Select(p => p.Id.ToString())).Concat(Loose.Select(p => p.Projection.Id.ToString())).ToArray();
            RememberRound(); await ResetAndCheck(); Require(Loose.Length == 0, "Client service loose state survived reset."); return;
        }
        var state = JsonSerializer.Deserialize<ServiceState>(samples)!;
        var bodies = JsonSerializer.Deserialize<Sample[]>(state.Bodies)!;
        var parts = JsonSerializer.Deserialize<FieldSample[]>(state.Loose)!;
        await Until(() => Matches(bodies, false) && Loose.Length == parts.Length && parts.All(s => Loose.Any(p =>
            p.NetworkObject.Id.Value == s.ObjectId && p.Projection.Id.Value.ToString("D") == s.PartId &&
            p.Projection.Model == s.Model && p.Projection.Condition == s.Condition && p.Carry.HolderNetworkObjectId == s.Holder &&
            p.Carry.WorldTransform.Origin.DistanceTo(new(s.X, s.Y, s.Z)) < 0.02f)));
        foreach (var body in Bodies)
        {
            CheckBody(body);
            Require(Service(body).GetNode<Label>("HUD/SlotLabel").Visible == body.GetNode("Input").IsMultiplayerAuthority(), "Service label leaked to another owner.");
            if (command != "service_fresh") Require(body.Gas.Snapshot.Energy == 6 && body.Gas.Snapshot.Heat == 12, "Client service resources reset.");
        }
        var local = LocalBody;
        switch (command)
        {
            case "service_detach":
                _clientServicePart = local.GetPart(MechSlot.LeftLeg)!.Id;
                await SelectServiceSlot(MechSlot.LeftLeg); await Pulse("service_part");
                await Until(() => !local.HasPart(MechSlot.LeftLeg) && Loose.Any(p => p.Projection.Id == _clientServicePart));
                ServiceGas(local, 1); break;
            case "service_move": await MeasureOwner("service_client_detached", "move_left"); break;
            case "service_return": await DriveOwner("move_right"); await Settle(); break;
            case "service_reinstall":
                var oldObject = LoosePart(_clientServicePart).NetworkObject.Id;
                await PickupServicePart(_clientServicePart); await Pulse("service_part");
                await Until(() => local.GetPart(MechSlot.LeftLeg)?.Id == _clientServicePart && !_world.TryGet(oldObject, out _));
                Require(!local.GetNode<PlayerCarrier>("PlayerCarrier").HasCarriedItem, "Client reinstall retained holder.");
                ServiceGas(local, 2); break;
            case "service_approach":
                Input.ActionPress("move_left"); await Ticks(12); Input.ActionRelease("move_left"); await Settle(); break;
            case "service_replace":
                var incoming = Loose.Single(p => p.Projection.Model == "debug_leg"); var id = incoming.Projection.Id; var obj = incoming.NetworkObject.Id;
                await PickupServicePart(id); await Pulse("service_part");
                await Until(() => local.GetPart(MechSlot.LeftLeg)?.Id == id && !_world.TryGet(obj, out _) && Loose.Any(p => p.Projection.Id == _clientServicePart));
                Require(!local.GetNode<PlayerCarrier>("PlayerCarrier").HasCarriedItem, "Client replacement retained holder."); break;
            case "service_attack":
                var other = Bodies.Single(b => !b.GetNode("Input").IsMultiplayerAuthority());
                Service(other).RequestService(MechSlot.LeftLeg); // local owner gate
                Service(other).RpcId(1, "RequestServiceRpc", (int)MechSlot.LeftLeg); // deliberately bypass the local gate
                Service(local).RpcId(1, "RequestServiceRpc", 99);
                bool rejected = false;
                try { Service(local).TryService(new(1), MechSlot.LeftLeg); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Client ran authoritative service.");
                await Ticks(6); break;
            case "service_fresh":
                CheckFresh(2);
                Require(_world.Objects.All(o => o.Id.Value > _oldMax) && Bodies.SelectMany(b => b.View.Parts.Select(p => p.Id.ToString()))
                    .Concat(Loose.Select(p => p.Projection.Id.ToString())).All(id => !_serviceOldIds.Contains(id)), "Client fresh round reused identity.");
                GD.Print("SERVICE_CLIENT_PASS owner_requests=true forged_owner_rejected=true body_gas_loose_converged=true rounds=2"); break;
        }
        GD.Print($"SERVICE_OBSERVED command={command} body_gas_loose_matched=true parts={Loose.Length}");
    }
}
