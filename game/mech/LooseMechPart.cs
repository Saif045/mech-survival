using System;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;

namespace MechSurvival.Mech;

/// <summary>One world embodiment of a server part. Carry state belongs to CarryTarget.</summary>
public partial class LooseMechPart : Node3D, INetworkSpawnInitializable
{
    public const string ScenePath = "res://game/mech/loose_mech_part.tscn";
    [Export] public MechPartCatalogResource Catalog { get; set; } = null!;
    public LoosePartProjection Projection { get; private set; } = null!;
    private PartInstance? _serverPart;
    public CarryTarget Carry => GetNode<CarryTarget>("CarryTarget");
    public NetworkObject NetworkObject => GetNode<NetworkObject>("NetworkObject");
    public bool InitializedBeforeTree { get; private set; }

    public static LooseMechPart Spawn(NetworkWorld world, PartInstance part, Transform3D transform)
    {
        if (!world.Multiplayer.IsServer()) throw new InvalidOperationException("Only the server may embody a part.");
        return world.Spawn<LooseMechPart>(GD.Load<PackedScene>(ScenePath), PeerId.Server,
            new Godot.Collections.Dictionary
            {
                ["mech_part_id"] = part.Id.Value.ToString("D"), ["model"] = part.Definition.Model,
                ["condition"] = part.Condition, ["spawn_transform"] = transform
            }, loose => loose.BindServerPart(part));
    }
    public void ApplyNetworkSpawnData(Variant data)
    {
        if (IsInsideTree() || Projection is not null) throw new InvalidOperationException("Spawn projection is initialized once before tree entry.");
        var fields = data.AsGodotDictionary();
        var projection = new LoosePartProjection(new(Guid.Parse(fields["mech_part_id"].AsString())),
            fields["model"].AsString(), fields["condition"].AsSingle(), Catalog.Freeze());
        Transform3D transform = fields["spawn_transform"].AsTransform3D();
        if (!transform.IsFinite()) throw new ArgumentException("Invalid loose part transform.");
        Projection = projection;
        Transform = transform;
        Carry.WorldTransform = transform;
        InitializedBeforeTree = true;
    }
    private void BindServerPart(PartInstance part)
    {
        // Configure is called by NetworkWorld only on the server, before tree entry.
        Projection.ValidateInstance(part, Catalog.Freeze());
        _serverPart = part;
    }
    public PartInstance GetServerPart()
    {
        if (!IsInsideTree() || !Multiplayer.IsServer() || !NetworkObject.IsBound)
            throw new InvalidOperationException("Only bound server gameplay may access the runtime part.");
        return _serverPart ?? throw new InvalidOperationException("Missing authoritative part.");
    }
    public override void _Ready()
    {
        if (Projection is null || !InitializedBeforeTree) throw new InvalidOperationException("Loose part requires spawn projection.");
        if (Multiplayer.IsServer()) Projection.ValidateInstance(GetServerPart(), Catalog.Freeze());
        else if (_serverPart is not null) throw new InvalidOperationException("Client received a mutable part.");
        GetNode<Label3D>("Label").Text = $"{Projection.Model}\n{Projection.Condition:P0}";
        var mesh = GetNode<MeshInstance3D>("Mesh");
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = Projection.Model == "debug_leg" ? Colors.Orange : Colors.Cyan };
    }
}
