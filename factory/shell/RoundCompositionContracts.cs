using Godot;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;

namespace GameFactory.Shell;

/// <summary>
/// Server-only dynamic content creation in the lifecycle-owned world.
/// ResetRound clears local state/subscriptions on every peer after world-clear,
/// or on terminal Stop (where network objects stay alive until peer teardown).
/// It must not spawn/despawn objects, reset identities or own session/lifecycle.
/// </summary>
public interface IRoundContent
{
    void StartRound(NetworkWorld world);
    void ResetRound();
}

/// <summary>Authoritative initialization data only; no spawning, identity or authority ownership.</summary>
public interface IPlayerSpawnDataProvider
{
    Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId playerId);
}
