using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;

namespace GameFactory.Sandbox.Carry;

/// <summary>Only pose/spawn data; no character, vitality or item gameplay model.</summary>
public partial class CarryFixtureActor : Node3D, INetworkSpawnInitializable
{
    [Replicated(ReplicationMode.OnChange)]
    public Transform3D ActorPose { get => Transform; set => Transform = value; }
    public void ApplyNetworkSpawnData(Variant data) => Position = data.AsGodotDictionary()["spawn_position"].AsVector3();
}
