using System;
using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Sandbox.Shell.RoundComposition;

public partial class RoundFixturePlayer : Node3D, INetworkSpawnInitializable
{
    public Vector3 SpawnPosition { get; private set; }
    public long PlayerNumber { get; private set; }
    public string Loadout { get; private set; } = "";
    public bool InitializedBeforeTree { get; private set; }
    public Action? OnExit { get; set; }
    public void ApplyNetworkSpawnData(Variant data)
    {
        var values = data.AsGodotDictionary();
        SpawnPosition = values["spawn_position"].AsVector3();
        PlayerNumber = values["player_number"].AsInt64();
        Loadout = values["loadout"].AsString();
        InitializedBeforeTree = !IsInsideTree();
        Position = SpawnPosition;
    }
    public override void _ExitTree() => OnExit?.Invoke();
}
