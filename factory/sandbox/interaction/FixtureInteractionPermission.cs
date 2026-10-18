using Godot;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Sandbox.Interaction;

/// <summary>No vitality dependency; server-only changes test canonical denial.</summary>
public partial class FixtureInteractionPermission : Node, IInteractionPermission
{
    public bool Allowed { get; set; } = true;
    public bool CanRequestInteraction(NetworkObject actor, PeerId requestingPeerId) => Allowed;
}
