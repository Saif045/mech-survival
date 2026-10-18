using GameFactory.Gameplay.Carry;
using Godot;
using GameFactory.Starter.Gameplay.Gas;
using GameFactory.Starter.Player;
using GameFactory.Networking.Objects;

namespace GameFactory.Starter.Gameplay.Carry;

/// <summary>Reference-player GAS policy, outside reusable carry.</summary>
public partial class StarterPlayerCarryPermission : Node, ICarryPermission
{
    public bool CanPickup(NetworkObject actor) => Allowed(actor, "pickup");
    public bool CanDrop(NetworkObject actor) => Allowed(actor, "drop");
    private bool Allowed(NetworkObject actor, string action)
    {
        NetworkPlayer3D player = GetParent<NetworkPlayer3D>();
        return Multiplayer.IsServer()
            ? !player.GetNode<NetworkGasComponent>("NetworkGasComponent").RejectIncapacitated(actor.OwnerPeerId, action)
            : !player.IsIncapacitated;
    }
}
