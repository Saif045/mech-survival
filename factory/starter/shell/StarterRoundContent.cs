using GameFactory.Shell;
using GameFactory.Starter.Gameplay.Carry;
using GameFactory.Starter.Gameplay.Interaction;
using Godot;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;

namespace GameFactory.Starter.Shell;

/// <summary>Starter/reference switch and cube content; not required by round lifecycle.</summary>
public partial class StarterRoundContent : Node, IRoundContent
{
    [Export] public PackedScene SwitchScene { get; set; } = null!;
    [Export] public PackedScene CarryableScene { get; set; } = null!;

    public void StartRound(NetworkWorld world)
    {
        world.Spawn<InteractableSwitch>(SwitchScene, PeerId.Server, new Godot.Collections.Dictionary
        { ["spawn_position"] = new Vector3(0, 0.625f, -1.5f) });
        world.Spawn<CarryableItem>(CarryableScene, PeerId.Server, new Godot.Collections.Dictionary
        { ["spawn_transform"] = new Transform3D(Basis.Identity, new Vector3(0, 0.35f, 1.5f)) });
    }
    public void ResetRound() { } // No retained local state; world teardown owns spawned objects.
}
