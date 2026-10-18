using System;
using Godot;
using GameFactory.Networking.World;
using GameFactory.Shell;
using MechSurvival.Mech;
using MechSurvival.Salvage;

namespace MechSurvival.Shell;

public partial class FieldPartsRoundContent : Node, IRoundContent
{
    [Export] public MechPartCatalogResource Catalog { get; set; } = null!;
    public void StartRound(NetworkWorld world)
    {
        if (!Multiplayer.IsServer()) throw new InvalidOperationException("Only server content may spawn loot.");
        SalvageMachine.Spawn(world, new Transform3D(Basis.Identity, new Vector3(-2, 1, -4)));
        LooseMechPart.Spawn(world, new PartInstance(Catalog.Freeze().Resolve("debug_leg"), 0.8f),
            new Transform3D(Basis.Identity, new Vector3(-2, 0.6f, -1.7f)));
        LooseMechPart.Spawn(world, new PartInstance(Catalog.Freeze().Resolve("debug_power"), 1),
            new Transform3D(Basis.Identity, new Vector3(2, 0.6f, -1.7f)));
    }
    public void ResetRound() { } // NetworkWorld owns removal; this node stores no round state.
}
