using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Shell;

namespace MechSurvival.Shell;

/// <summary>Game-owned initial body placement; lifecycle and identity remain factory-owned.</summary>
public partial class MechSpawnDataProvider : Node, IPlayerSpawnDataProvider
{
    public Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId playerId) => new()
    {
        ["spawn_position"] = playerId.Value == 1 ? new Vector3(-2, 2, 0) : new Vector3(2, 2, 0)
    };
}
