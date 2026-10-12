using System;
using Godot;
using GameFactory.Networking.Objects;
using MechSurvival.Mech;

namespace MechSurvival.Player;

/// <summary>The player's initial operated body; player identity is separate.</summary>
public partial class PlayableMech : NetworkMechBody, INetworkSpawnInitializable
{
    public Vector3 InitialSpawnPosition { get; private set; }
    public bool InitializedBeforeTree { get; private set; }
    // Read-only views of the already canonical/mirrored snapshot. No new state,
    // replication fields, structural calculation or rollback writes to GAS.
    public float GasMobility => Gas.Snapshot.Mobility;
    public bool GasHasMobility => Gas.Snapshot.HasMobility;
    public float GasMoveSpeed => MobilityMovementPolicy.Speed(GasMobility, GasHasMobility);

    public void ApplyNetworkSpawnData(Variant data)
    {
        InitialSpawnPosition = data.AsGodotDictionary()["spawn_position"].AsVector3();
        Position = InitialSpawnPosition;
        InitializedBeforeTree = !IsInsideTree();
    }

    public override void _Ready()
    {
        base._Ready();
        if (!Multiplayer.IsServer()) return;
        foreach (MechSlot slot in MechBody.Slots)
        {
            string model = slot switch
            {
                MechSlot.LeftArm or MechSlot.RightArm => "debug_arm",
                MechSlot.LeftLeg or MechSlot.RightLeg => "debug_leg",
                _ => "debug_" + slot.ToString().ToLowerInvariant()
            };
            if (!AttachPart(slot, CreatePart(model)))
                throw new InvalidOperationException($"Default playable loadout failed at {slot}.");
        }
        // Fresh playable policy only. Generic bodies still start empty/depleted.
        Gas.InitializeEnergyFull();
    }
}
