using Godot;
using GameFactory.Shell;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;

namespace GameFactory.Sandbox.Shell.RoundComposition;

public partial class RoundFixtureSpawnData : Node, IPlayerSpawnDataProvider
{
    public int Calls { get; private set; }
    public Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId playerId)
    {
        Calls++;
        return new()
        {
            ["spawn_position"] = new Vector3(playerId.Value == 1 ? -6 : 6, 3, 4),
            ["player_number"] = playerId.Value,
            ["loadout"] = peer.IsLocal ? "scout" : "engineer"
        };
    }
}
