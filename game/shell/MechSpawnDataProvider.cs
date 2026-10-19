using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Shell;

namespace MechSurvival.Shell;

/// <summary>Game-owned initial body placement; lifecycle and identity remain factory-owned.</summary>
public partial class MechSpawnDataProvider : Node, IPlayerSpawnDataProvider
{
    public static readonly Vector3[] Positions = [new(-2, 2, 8), new(2, 2, 8), new(-2, 2, 10), new(2, 2, 10)];
    public Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId playerId) => new()
    {
        ["spawn_position"] = Positions[(int)((playerId.Value - 1) % 4)]
    };
}
