using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Carry;

public partial class CarryFixturePolicy : Node, ICarryPermission, IInteractionPermission
{
    public bool PickupAllowed { get; set; } = true;
    public bool DropAllowed { get; set; } = true;
    public bool CanPickup(NetworkObject actor) => PickupAllowed;
    public bool CanDrop(NetworkObject actor) => DropAllowed;
    public bool CanRequestInteraction(NetworkObject actor, PeerId peer) => true;
}
