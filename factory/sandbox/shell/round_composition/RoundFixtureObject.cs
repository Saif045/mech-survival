using Godot;
using GameFactory.Networking.Objects;

namespace GameFactory.Sandbox.Shell.RoundComposition;

public partial class RoundFixtureObject : Node3D, INetworkSpawnInitializable
{
    public string Kind { get; private set; } = "";
    public int RoundValue { get; private set; }
    public bool InitializedBeforeTree { get; private set; }
    public void ApplyNetworkSpawnData(Variant data)
    {
        var values = data.AsGodotDictionary();
        Kind = values["kind"].AsString();
        RoundValue = values["round_value"].AsInt32();
        Position = values["spawn_position"].AsVector3();
        InitializedBeforeTree = !IsInsideTree();
    }
}
