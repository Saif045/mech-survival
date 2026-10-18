using System;
using System.Collections.Generic;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Carry;

/// <summary>One-item capacity and reliable owner-to-server drop requests.</summary>
public partial class PlayerCarrier : Node, ICarryHolder
{
    [Export] public Vector3 DropOffset { get; set; } = new(0, 0.5f, -1.25f);
    [Export] public NodePath AnchorPath { get; set; } = new("");
    [Export] public NodePath PermissionPath { get; set; } = new("");
    [Export] public bool EnableDefaultInput { get; set; } = true;
    private NetworkObject _player = null!;
    private Node3D _playerHost = null!;
    private Node3D _carryAnchor = null!;
    private ICarryPermission? _permission;
    private long _carriedItemId;
    public NetworkObject NetworkObject => _player;
    public Node3D CarryAnchor => _carryAnchor;
    public bool HasCarriedItem => Multiplayer.IsServer() ? _carriedItemId > 0 : FindHeldItem() is not null;

    public override void _Ready()
    {
        _playerHost = GetParent<Node3D>();
        _player = _playerHost.GetNode<NetworkObject>("NetworkObject");
        if (AnchorPath.IsEmpty || GetNodeOrNull<Node>(AnchorPath) is not Node3D anchor)
            throw new InvalidOperationException("PlayerCarrier requires AnchorPath to reference a Node3D.");
        _carryAnchor = anchor;
        if (PermissionPath.IsEmpty || GetNodeOrNull<Node>(PermissionPath) is not ICarryPermission policy)
            throw new InvalidOperationException("PlayerCarrier requires PermissionPath to reference an ICarryPermission node.");
        _permission = policy;
    }
    public override void _Process(double delta)
    {
        if (EnableDefaultInput && Input.IsActionJustPressed("drop_item")) RequestDrop();
    }
    public void RequestDrop()
    {
        if (!IsLocalOwner() || _permission is null || !_permission.CanDrop(_player) || !HasCarriedItem) return;
        if (Multiplayer.IsServer()) HandleDrop(PeerId.Server);
        else RpcId(PeerId.Server.Value, MethodName.RequestDropRpc);
    }
    public override void _ExitTree()
    {
        if (_player is not null && Multiplayer.HasMultiplayerPeer() && Multiplayer.IsServer() && _carriedItemId > 0)
            ReleaseHeldItem("carrier_exit");
    }
    public bool TryPickup(ICarryTarget item)
    {
        RequireServer();
        // A removed item cannot permanently occupy the server's one-item cache.
        if (_carriedItemId > 0 && (!_player.World.TryGet(new NetworkObjectId(_carriedItemId), out NetworkObject? held) ||
            !IsRegistered(held))) _carriedItemId = 0;
        if (_permission is null || !GodotObject.IsInstanceValid(_carryAnchor) || !_permission.CanPickup(_player))
        { Log("rejected", item, "pickup_permission_denied"); return false; }
        if (HasCarriedItem) { Log("rejected", item, "carrier_already_holding"); return false; }
        NetworkObject target = item.NetworkObject;
        if (!IsRegistered(target) || target == _player || target.Host.GetMultiplayerAuthority() != PeerId.Server.Value ||
            !item.IsAvailable || item.HolderNetworkObjectId != 0)
        { Log("rejected", item, "item_not_available"); return false; }
        if (!item.TryAttach(_player)) return false;
        _carriedItemId = target.Id.Value;
        Log("picked_up", item, "authority_change");
        return true;
    }
    /// <summary>Server lifecycle release bypasses input permission, not holder validation.</summary>
    public void ReleaseHeldItem(string reason = "lifecycle")
    {
        RequireServer();
        if (_carriedItemId > 0) Release(reason);
    }
    /// <summary>Reconcile after a game-owned transition removed carry ownership.</summary>
    public void ForgetHeldItem(ICarryTarget item)
    {
        RequireServer();
        if (item.HolderNetworkObjectId == _player.Id.Value)
            throw new InvalidOperationException("Release/transition the item before forgetting it.");
        if (_carriedItemId == item.NetworkObject.Id.Value) _carriedItemId = 0;
    }
    /// <summary>Reconcile after a game-owned transition established carry ownership.</summary>
    public void AdoptHeldItem(ICarryTarget item)
    {
        RequireServer();
        if (HasCarriedItem || !IsRegistered(item.NetworkObject) || item.HolderNetworkObjectId != _player.Id.Value)
            throw new InvalidOperationException("Cannot adopt an occupied/unregistered/mismatched item.");
        _carriedItemId = item.NetworkObject.Id.Value;
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestDropRpc()
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender <= 0) { Log("rejected", null, "invalid_rpc_sender"); return; }
        HandleDrop(new PeerId(sender));
    }
    private void HandleDrop(PeerId sender, string source = "request")
    {
        RequireServer();
        if (_player.OwnerPeerId != sender) { Log("rejected", null, "sender_is_not_player_owner"); return; }
        if (_permission is null || !_permission.CanDrop(_player)) { Log("rejected", null, "drop_permission_denied"); return; }
        Release(source);
    }
    private void Release(string reason)
    {
        if (_carriedItemId <= 0) { Log("rejected", null, "player_not_carrying"); return; }
        if (!_player.World.TryGet(new NetworkObjectId(_carriedItemId), out NetworkObject? obj) ||
            !IsRegistered(obj) || CarryCapabilities.Target(obj!.Host) is not ICarryTarget item)
        { _carriedItemId = 0; Log("rejected", null, "carried_item_missing"); return; }
        if (item.HolderNetworkObjectId != _player.Id.Value || obj.Host.GetMultiplayerAuthority() != PeerId.Server.Value)
        { _carriedItemId = 0; Log("rejected", item, "item_holder_mismatch"); return; }
        Transform3D transform = _playerHost.GlobalTransform;
        transform.Origin += transform.Basis * DropOffset;
        if (!item.TryRelease(_player, transform)) { Log("rejected", item, "item_release_rejected"); return; }
        _carriedItemId = 0;
        Log("dropped", item, reason);
    }
    private bool IsRegistered(NetworkObject? obj) => obj is not null && GodotObject.IsInstanceValid(obj) && obj.IsBound &&
        obj.World == _player.World && obj.Host.IsInsideTree() && !obj.Host.IsQueuedForDeletion() &&
        _player.World.TryGet(obj.Id, out NetworkObject? registered) && registered == obj;
    private ICarryTarget? FindHeldItem()
    {
        if (_player is null || !_player.IsBound) return null;
        foreach (NetworkObject obj in _player.World.Objects)
            if (CarryCapabilities.Target(obj.Host) is ICarryTarget item && item.HolderNetworkObjectId == _player.Id.Value) return item;
        return null;
    }
    private bool IsLocalOwner() => _player is not null && _player.IsBound && Multiplayer.HasMultiplayerPeer() &&
        _player.OwnerPeerId.Value == Multiplayer.GetUniqueId();
    private void RequireServer()
    {
        if (!Multiplayer.IsServer()) throw new InvalidOperationException("Only the server may mutate carry state.");
    }
    private void Log(string eventName, ICarryTarget? item, string reason) =>
        GameLog.Info("carry", eventName, fields: new Dictionary<string, string?>
        {
            ["player_network_object_id"] = _player.IsBound ? _player.Id.ToString() : null,
            ["item_network_object_id"] = item?.NetworkObject.IsBound == true ? item.NetworkObject.Id.ToString() : null,
            ["reason"] = reason,
            ["owner_peer_id"] = _player.IsBound ? _player.OwnerPeerId.ToString() : null,
            ["role"] = Multiplayer.IsServer() ? "host" : "client"
        });
}
