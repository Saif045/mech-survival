using System;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Sandbox.Shell;

/// <summary>Minimal player fixture without sample movement or gameplay components.</summary>
public partial class CustomPlayerHost : Node, INetworkSpawnInitializable
{
    public Vector3 SpawnPosition { get; private set; }
    public bool InitializedBeforeTree { get; private set; }
    public Action? OnExit { get; set; }

    public void ApplyNetworkSpawnData(Variant data)
    {
        SpawnPosition = data.AsGodotDictionary()["spawn_position"].AsVector3();
        InitializedBeforeTree = !IsInsideTree();
    }

    public override void _ExitTree() => OnExit?.Invoke();
}
