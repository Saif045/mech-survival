using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Carry;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using MechSurvival.Mech;

namespace MechSurvival.Player;

/// <summary>Owner-selected slot requests; all items, instances and transforms are derived on the server.</summary>
public partial class MechServiceController : Node
{
    public MechSlot SelectedSlot { get; private set; } = MechSlot.Core;
    private PlayableMech _body = null!;
    private NetworkObject _actor = null!;
    private PlayerCarrier _carrier = null!;

    public override void _Ready()
    {
        _body = GetParent<PlayableMech>();
        _actor = _body.GetNode<NetworkObject>("NetworkObject");
        _carrier = _body.GetNode<PlayerCarrier>("PlayerCarrier");
        BuildPanel();
    }
    public override void _Process(double delta)
    {
        if (IsLocalOwner() && Input.IsActionJustPressed("mech_status")) SetPanelOpen(!PanelOpen);
        RefreshPanel();
    }
    private bool IsLocalOwner() => _actor is not null && _actor.IsBound && Multiplayer.HasMultiplayerPeer() &&
        _actor.OwnerPeerId.Value == Multiplayer.GetUniqueId();

    public void RequestInstall(MechSlot slot) => Request(slot, MechServiceIntent.Install);
    public void RequestDetach(MechSlot slot) => Request(slot, MechServiceIntent.Detach);
    private void Request(MechSlot slot, MechServiceIntent intent)
    {
        if (!IsLocalOwner()) return;
        if (Multiplayer.IsServer()) TryService(PeerId.Server, slot, intent);
        else RpcId(PeerId.Server.Value, intent == MechServiceIntent.Install ? MethodName.RequestInstallRpc : MethodName.RequestDetachRpc, (int)slot);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestInstallRpc(int slot)
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender > 0) TryInstall(new PeerId(sender), (MechSlot)slot);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDetachRpc(int slot)
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender > 0) TryDetach(new PeerId(sender), (MechSlot)slot);
    }
    internal bool TryInstall(PeerId sender, MechSlot slot) => TryService(sender, slot, MechServiceIntent.Install);
    internal bool TryDetach(PeerId sender, MechSlot slot) => TryService(sender, slot, MechServiceIntent.Detach);
    private bool TryService(PeerId sender, MechSlot slot, MechServiceIntent intent)
    {
        if (!IsInsideTree() || !Multiplayer.IsServer()) throw new InvalidOperationException("Service requires the server.");
        if (!Enum.IsDefined(slot) || !_actor.IsBound || _body.IsQueuedForDeletion() ||
            _body.GetMultiplayerAuthority() != PeerId.Server.Value || _actor.OwnerPeerId != sender ||
            !_actor.World.TryGet(_actor.Id, out var registered) || registered != _actor)
            return Result(false, slot, "invalid_actor_sender_or_slot");

        if (intent == MechServiceIntent.Detach)
        {
            if (!MechServiceIntentPolicy.Allows(intent, false, _body.HasPart(slot), false)) return Result(false, slot, "empty_slot");
            var detached = _body.DetachPart(slot)!;
            try { LooseMechPart.Spawn(_actor.World, detached, ServiceDropTransform()); }
            catch
            {
                if (!_body.AttachPart(slot, detached)) throw new InvalidOperationException("Failed to restore detached part after spawn failure.");
                throw;
            }
            return Result(true, slot, "detached");
        }
        LooseMechPart? incoming = null;
        foreach (var obj in _actor.World.Objects)
        {
            if (obj.Host is not LooseMechPart part || part.Carry.HolderNetworkObjectId != _actor.Id.Value) continue;
            if (incoming is not null || !obj.IsBound || obj.World != _actor.World || obj.OwnerPeerId != PeerId.Server ||
                part.IsQueuedForDeletion() || part.GetMultiplayerAuthority() != PeerId.Server.Value)
                return Result(false, slot, "invalid_carried_object");
            incoming = part;
        }
        if (incoming is null) return Result(false, slot, "install_requires_carried_part");

        // Complete policy validation before any body/carry/world mutation.
        var instance = incoming.GetServerPart();
        incoming.Projection.ValidateInstance(instance, incoming.Catalog.Freeze());
        if (!_carrier.HasCarriedItem || !MechServiceIntentPolicy.Allows(intent, true, _body.HasPart(slot), _body.CanReplacePart(slot, instance)))
            return Result(false, slot, "incompatible_or_unavailable_part");
        Transform3D drop = ServiceDropTransform();
        if (!incoming.Carry.TryRelease(_actor, drop)) return Result(false, slot, "release_rejected");
        _carrier.ForgetHeldItem(incoming.Carry);
        if (!_body.ReplacePart(slot, instance, out var previous))
        {
            RestoreCarry(incoming);
            return Result(false, slot, "install_rejected");
        }
        try
        {
            if (previous is not null) LooseMechPart.Spawn(_actor.World, previous, drop);
        }
        catch
        {
            // Only the outgoing spawn can fail here. Restore the same two instances.
            if (!ReferenceEquals(_body.DetachPart(slot), instance) ||
                (previous is not null && !_body.AttachPart(slot, previous)))
                throw new InvalidOperationException("Failed to restore replaced body part after spawn failure.");
            RestoreCarry(incoming);
            throw;
        }
        _actor.World.Despawn(incoming.NetworkObject.Id);
        return Result(true, slot, previous is null ? "installed" : "replaced");
    }
    private void RestoreCarry(LooseMechPart incoming)
    {
        if (!_carrier.TryPickup(incoming.Carry)) throw new InvalidOperationException("Failed to restore incoming carry ownership.");
    }
    private Transform3D ServiceDropTransform()
    {
        Transform3D transform = _body.GlobalTransform;
        // Beside the body, within interaction range, outside its collision capsule.
        transform.Origin += transform.Basis * new Vector3(1.3f, -0.45f, -0.9f);
        return transform;
    }
    private bool Result(bool accepted, MechSlot slot, string reason)
    {
        GameLog.Info("mech.service", accepted ? "accepted" : "rejected", fields: new Dictionary<string, string?>
        {
            ["body"] = _actor.Id.ToString(), ["slot"] = slot.ToString(), ["reason"] = reason
        });
        return accepted;
    }
}
