using System;
using Godot;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Carry;

/// <summary>World/carried state on a direct item child; no stored/equipped states.</summary>
public partial class CarryTarget : Node, ICarryTarget, IInteractable
{
    [Export] public NodePath CollisionPath { get; set; } = new("");
    [Export] public long HolderNetworkObjectId { get; set; }
    [Export] public Transform3D WorldTransform { get; set; } = Transform3D.Identity;
    public NetworkObject NetworkObject { get; private set; } = null!;
    public bool IsAvailable => _synchronizer is not null && HolderNetworkObjectId == 0;
    private Node3D _host = null!;
    private CollisionShape3D _collision = null!;
    private MultiplayerSynchronizer _synchronizer = null!;

    public override void _Ready()
    {
        _host = GetParent<Node3D>();
        NetworkObject = _host.GetNode<NetworkObject>("NetworkObject");
        if (CollisionPath.IsEmpty || GetNodeOrNull<Node>(CollisionPath) is not CollisionShape3D collision)
            throw new InvalidOperationException("CarryTarget requires CollisionPath to reference a CollisionShape3D.");
        _collision = collision;
        _host.SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        SetMultiplayerAuthority((int)PeerId.Server.Value, recursive: false);
        SceneReplicationConfig config = new();
        foreach (string property in new[] { nameof(HolderNetworkObjectId), nameof(WorldTransform) })
        {
            NodePath path = new($".:{property}");
            config.AddProperty(path);
            config.PropertySetSpawn(path, true);
            config.PropertySetReplicationMode(path, SceneReplicationConfig.ReplicationMode.OnChange);
        }
        _synchronizer = new MultiplayerSynchronizer { Name = "CarryState", RootPath = new NodePath(".."), ReplicationConfig = config };
        _synchronizer.Synchronized += ApplyState;
        _synchronizer.DeltaSynchronized += ApplyState;
        AddChild(_synchronizer);
        ApplyState();
    }

    public bool CanInteract(InteractionContext context) => IsAvailable &&
        CarryCapabilities.Holder(context.Player.Host) is { HasCarriedItem: false };
    public void Interact(InteractionContext context)
    {
        RequireServer();
        if (CanInteract(context)) CarryCapabilities.Holder(context.Player.Host)?.TryPickup(this);
    }
    public bool TryAttach(NetworkObject holder)
    {
        RequireServer();
        if (!IsAvailable || holder == NetworkObject || holder.World != NetworkObject.World ||
            CarryCapabilities.Holder(holder.Host) is not ICarryHolder) return false;
        HolderNetworkObjectId = holder.Id.Value;
        ApplyState();
        return true;
    }
    public bool TryRelease(NetworkObject holder, Transform3D worldTransform)
    {
        RequireServer();
        if (holder.World != NetworkObject.World || HolderNetworkObjectId != holder.Id.Value) return false;
        HolderNetworkObjectId = 0;
        WorldTransform = worldTransform;
        ApplyState();
        return true;
    }
    public override void _Process(double delta)
    {
        if (HolderNetworkObjectId > 0 && NetworkObject.World.TryGet(new NetworkObjectId(HolderNetworkObjectId), out NetworkObject? holder) &&
            CarryCapabilities.Holder(holder!.Host) is ICarryHolder carrier && GodotObject.IsInstanceValid(carrier.CarryAnchor))
            _host.GlobalTransform = carrier.CarryAnchor.GlobalTransform;
    }
    private void ApplyState()
    {
        if (HolderNetworkObjectId == 0)
        {
            _host.GlobalTransform = WorldTransform;
            _host.AddToGroup("interactable");
        }
        else _host.RemoveFromGroup("interactable");
        _collision.SetDeferred(CollisionShape3D.PropertyName.Disabled, HolderNetworkObjectId > 0);
    }
    private void RequireServer()
    {
        if (!Multiplayer.IsServer()) throw new InvalidOperationException("Only the server may attach/release carry targets.");
    }
    public override void _ExitTree()
    {
        if (_synchronizer is null) return;
        _synchronizer.Synchronized -= ApplyState;
        _synchronizer.DeltaSynchronized -= ApplyState;
    }
}
