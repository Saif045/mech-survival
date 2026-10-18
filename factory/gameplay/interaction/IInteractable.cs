using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Gameplay.Interaction;

/// <summary>
/// The small contract for an object a player can ask to use. CanInteract is a
/// side-effect-free local availability hint during candidate selection; its
/// server invocation is decisive. Interact is dispatched only after the
/// server resolves and validates the request.
/// </summary>
public interface IInteractable
{
    bool CanInteract(InteractionContext context);

    void Interact(InteractionContext context);
}

/// <summary>
/// Locally assembled availability data or server-assembled request data.
/// Only the server invocation is authoritative; no transform/state is sent
/// by the client. ServerPlayerPosition retains its name for compatibility,
/// but contains the local actor position during local candidate selection.
/// </summary>
public readonly record struct InteractionContext(
    PeerId RequestingPeerId,
    NetworkObject Player,
    NetworkObject Target,
    Vector3 ServerPlayerPosition);
