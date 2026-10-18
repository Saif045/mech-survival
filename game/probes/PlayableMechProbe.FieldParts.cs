using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using MechSurvival.Mech;

namespace MechSurvival.Probes;

public partial class PlayableMechProbe
{
    private LooseMechPart[] Loose => _world.Objects.Select(o => o.Host).OfType<LooseMechPart>()
        .OrderBy(p => p.Projection.Model).ToArray();
    public sealed record FieldSample(long ObjectId, string PartId, string Model, float Condition,
        long Holder, float X, float Y, float Z);
    private string FieldCapture() => JsonSerializer.Serialize(Loose.Select(p => new FieldSample(
        p.NetworkObject.Id.Value, p.Projection.Id.Value.ToString("D"), p.Projection.Model, p.Projection.Condition,
        p.Carry.HolderNetworkObjectId, p.Carry.WorldTransform.Origin.X, p.Carry.WorldTransform.Origin.Y, p.Carry.WorldTransform.Origin.Z)));
    private async Task FieldStage(int stage, string command = "field_observe")
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, FieldCapture());
        await Until(() => _ack == stage);
    }
    private async Task FieldAcceptance(bool offline)
    {
        // Start before connecting the observer: exercise existing spawn + held-state replication.
        StartRound(); CheckFresh(1); CheckRegistry();
        Require(Loose.Length == 2 && Loose[0].Projection.Model == "debug_leg" && Loose[0].Projection.Condition == 0.8f &&
            Loose[1].Projection.Model == "debug_power" && Loose[1].Projection.Condition == 1, "Prototype content mismatch.");
        var first = Loose.ToArray();
        var instances = first.Select(p => p.GetServerPart()).ToArray();
        string[] ids = first.Select(p => p.Projection.Id.ToString()).ToArray();
        long[] objects = first.Select(p => p.NetworkObject.Id.Value).ToArray();
        foreach (var p in first) Require(p.InitializedBeforeTree && p.Carry.WorldTransform == p.Transform &&
            p.NetworkObject.OwnerPeerId.Value == 1, "Loose spawn composition/authority mismatch.");
        await Settle();
        var host = Bodies[0];
        var carrier = host.GetNode<PlayerCarrier>("PlayerCarrier");
        // Normal default semantic input selects nearby leg.
        Input.ActionPress("interact");
        await Until(() => first[0].Carry.HolderNetworkObjectId == Object(host).Id.Value);
        Input.ActionRelease("interact");
        Require(!carrier.TryPickup(first[1].Carry), "One-item capacity accepted another item.");
        await DriveOwner("move_left"); await Settle();
        await Until(() => first[0].GlobalPosition.DistanceTo(carrier.CarryAnchor.GlobalPosition) < 0.01f);
        GD.Print("FIELD_HOST_PASS default_interact=true capacity=true anchor_follow=true");
        GD.Print("PLAYABLE_READY role=server");
        if (!offline) { await Until(() => _hello); await FieldStage(101); }
        Input.ActionPress("drop_item");
        await Until(() => first[0].Carry.HolderNetworkObjectId == 0);
        Input.ActionRelease("drop_item");
        var expectedDrop = host.GlobalTransform;
        expectedDrop.Origin += expectedDrop.Basis * carrier.DropOffset;
        Require(first[0].Carry.WorldTransform.IsEqualApprox(expectedDrop), "Host drop transform mismatch.");
        for (int i = 0; i < first.Length; i++) Require(ReferenceEquals(instances[i], first[i].GetServerPart()) &&
            first[i].Projection.Id == instances[i].Id && first[i].NetworkObject.Id.Value == objects[i], "Carry/drop recreated an identity or instance.");
        if (!offline) await FieldStage(102);
        RememberRound();
        if (!offline) RpcId(_remote, MethodName.CommandRpc, 103, "field_reset", "");
        await ResetAndCheck();
        Require(Loose.Length == 0, "Round retained loose parts.");
        if (!offline) await Until(() => _ack == 103);
        StartRound(); CheckFresh(offline ? 1 : 2); CheckRegistry();
        Require(Loose.Length == 2 && Loose.All(p => !ids.Contains(p.Projection.Id.ToString()) && p.NetworkObject.Id.Value > objects.Max()),
            "Fresh round reused loose identity.");
        await Settle();
        if (!offline)
        {
            await FieldStage(104);
            await FieldStage(105, "field_pickup");
            var power = Loose[1]; var client = Bodies.Single(b => Object(b).OwnerPeerId.Value != 1);
            await Until(() => power.Carry.HolderNetworkObjectId == Object(client).Id.Value);
            await Until(() => power.GlobalPosition.DistanceTo(client.GetNode<PlayerCarrier>("PlayerCarrier").CarryAnchor.GlobalPosition) < 0.01f);
            await FieldStage(106, "field_move");
            await Settle();
            await FieldStage(107);
            var instance = power.GetServerPart(); long objectId = power.NetworkObject.Id.Value;
            await FieldStage(108, "field_drop");
            await Until(() => power.Carry.HolderNetworkObjectId == 0);
            var drop = client.GlobalTransform;
            drop.Origin += drop.Basis * client.GetNode<PlayerCarrier>("PlayerCarrier").DropOffset;
            Require(power.Carry.WorldTransform.IsEqualApprox(drop) && ReferenceEquals(instance, power.GetServerPart()) &&
                power.NetworkObject.Id.Value == objectId, "Client drop lost identity or transform.");
            await FieldStage(109);
            RpcId(_remote, MethodName.FinishRpc);
            await Ticks(3);
        }
        GD.Print("FIELD_ACCEPTANCE_PASS parts=2 stable_ids=true server_instances=true late_held_observation=true fresh_round=true");
    }
    private async Task FieldCommand(string command, string samples)
    {
        if (command == "field_reset") { await ResetAndCheck(); Require(Loose.Length == 0, "Client retained loose objects."); return; }
        var expected = JsonSerializer.Deserialize<FieldSample[]>(samples)!;
        await Until(() => Loose.Length == expected.Length && expected.All(s => Loose.Any(p =>
            p.NetworkObject.Id.Value == s.ObjectId && p.Projection.Id.Value.ToString("D") == s.PartId &&
            p.Projection.Model == s.Model && p.Projection.Condition == s.Condition && p.Carry.HolderNetworkObjectId == s.Holder &&
            p.Carry.WorldTransform.Origin.DistanceTo(new(s.X, s.Y, s.Z)) < 0.01f)));
        foreach (var p in Loose)
        {
            Require(p.InitializedBeforeTree && p.GetMultiplayerAuthority() == 1, "Client loose composition mismatch.");
            bool denied = false;
            try { p.GetServerPart(); } catch (InvalidOperationException) { denied = true; }
            Require(denied, "Client accessed server instance.");
            denied = false;
            try { p.Carry.TryAttach(Object(Bodies[0])); } catch (InvalidOperationException) { denied = true; }
            Require(denied, "Client attached authoritative target.");
            denied = false;
            try { p.Carry.TryRelease(Object(Bodies[0]), Transform3D.Identity); } catch (InvalidOperationException) { denied = true; }
            Require(denied, "Client released authoritative target.");
            if (p.Carry.HolderNetworkObjectId > 0)
                await Until(() => _world.TryGet(new(p.Carry.HolderNetworkObjectId), out var holder) &&
                    p.GlobalPosition.DistanceTo(holder!.Host.GetNode<PlayerCarrier>("PlayerCarrier").CarryAnchor.GlobalPosition) < 0.02f);
            else await Until(() => p.GlobalTransform.IsEqualApprox(p.Carry.WorldTransform));
        }
        await Until(() => Bodies.Length > 0 && Bodies.All(b => b.View.Parts.Count == 9 &&
            b.Gas.ReadSnapshot() == b.Gas.Snapshot && b.Gas.Snapshot.Serialize() == b.GasState));
        foreach (var body in Bodies) { CheckBody(body); AssertGuards(body); }
        if (command == "field_pickup")
        {
            var owner = Bodies.Single(b => b.GetNode("Input").IsMultiplayerAuthority());
            owner.GetNode<PlayerInteractor>("PlayerInteractor").RequestInteraction(Loose[1].NetworkObject.Id);
            await Until(() => Loose[1].Carry.HolderNetworkObjectId == Object(owner).Id.Value);
        }
        else if (command == "field_move") { await DriveOwner("move_forward"); await Settle(); }
        else if (command == "field_drop")
        {
            Bodies.Single(b => b.GetNode("Input").IsMultiplayerAuthority()).GetNode<PlayerCarrier>("PlayerCarrier").RequestDrop();
            await Until(() => Loose[1].Carry.HolderNetworkObjectId == 0);
        }
        GD.Print($"FIELD_OBSERVED command={command} parts=2 projection=true holder=true transform=true client_guards=true");
    }
}
