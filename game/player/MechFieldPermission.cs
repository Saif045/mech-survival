using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace MechSurvival.Player;

/// <summary>No incapacitation rule exists yet. Target, range and sender checks remain factory-owned.</summary>
public partial class MechFieldPermission : Node, IInteractionPermission, ICarryPermission
{
    public bool CanRequestInteraction(NetworkObject actor, PeerId requestingPeerId) => true;
    public bool CanPickup(NetworkObject actor) => true;
    public bool CanDrop(NetworkObject actor) => true;
}
