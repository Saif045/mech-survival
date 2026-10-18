using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Interaction;

/// <summary>
/// Actor-state permission only; target rules belong to IInteractable.
/// Called locally as an availability hint and on the server against canonical
/// state. Implementations must support both roles; local checks are side-effect-free.
/// Compose an explicit permissive policy for actors without state restrictions.
/// </summary>
public interface IInteractionPermission
{
    bool CanRequestInteraction(NetworkObject actor, PeerId requestingPeerId);
}
