using GameFactory.Gameplay.Interaction;
using Godot;
using GameFactory.Starter.Gameplay.Gas;
using GameFactory.Starter.Player;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

namespace GameFactory.Starter.Gameplay.Interaction;

/// <summary>Reference-player policy: canonical server GAS, local vital projection.</summary>
public partial class StarterPlayerInteractionPermission : Node, IInteractionPermission
{
    public bool CanRequestInteraction(NetworkObject actor, PeerId requestingPeerId)
    {
        NetworkPlayer3D player = GetParent<NetworkPlayer3D>();
        return Multiplayer.IsServer()
            ? !player.GetNode<NetworkGasComponent>("NetworkGasComponent").RejectIncapacitated(requestingPeerId, "interact")
            : !player.IsIncapacitated;
    }
}
