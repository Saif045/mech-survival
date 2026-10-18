using GameFactory.Shell;
using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;

namespace GameFactory.Starter.Shell;

/// <summary>Starter/reference positions and payload, selected explicitly by the default scene.</summary>
public partial class StarterPlayerSpawnDataProvider : Node, IPlayerSpawnDataProvider
{
    public Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId playerId) => new()
    {
        ["spawn_position"] = playerId.Value == 1 ? new Vector3(-2, 2, 0) : new Vector3(2, 2, 0)
    };
}
