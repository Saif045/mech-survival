# Reference player composition

[network_player_3d.tscn](network_player_3d.tscn) is the optional working
CharacterBody3D player. It composes mechanisms with starter gameplay semantics;
a custom player can use those mechanisms without inheriting NetworkPlayer3D.

```text
NetworkPlayer3D
  NetworkObject (ReplicationComponent, AuthorityComponent, NetfoxRollbackPlayerComponent)
  Input / Simulation / Presentation (VisualRoot, CarryAnchor)
  PlayerInteractor / InteractionPermission
  PlayerCarrier / CarryPermission
  NetworkGasComponent
  PlayerInventory                # exits before GAS in reverse child order
  PlayerReviveInteractable
  CollisionShape3D / RollbackSynchronizer / TickInterpolator / Camera3D
```

NetworkPlayer3D implements INetworkSpawnInitializable and requires a dictionary
with `spawn_position`; it retains OriginalSpawnPosition for respawn. The
[starter provider](../shell/StarterPlayerSpawnDataProvider.cs) owns that schema.
The [shell/world](../../shell/README.md) owns spawning/round teardown; the player
does not spawn itself. Represented-owner colors are deterministic local visuals.

[Input](network_player_3d_input.gd) gathers owner movement and queued actions.
[Simulation](network_player_3d_simulation.gd) performs server-authority physical
rollback. [Presentation](network_player_3d_presentation.gd) and
[Camera](network_player_3d_camera.gd) handle visuals; interpolation targets
Presentation:position. The prefab's visible Netfox lists record physical
position/velocity, grounded, dash state and consumed dash/respawn revisions;
inputs are movement, jump, dash and sprint. GAS, carry and inventory remain
ordinary state outside history. Use [Netfox](../../networking/netfox/README.md)
for the reusable topology contract, not all historical integration notes.

The host's Gas* properties, inventory object IDs and respawn revision/tick are
ordinary server projections. [GAS](../gameplay/gas/README.md) owns the starter
canonical vitality/effects and client mirroring. [Starter map](../README.md)
describes inventory, equipment and revive semantics. The player is not a
universal authority, inventory or GAS API.

PermissionPath explicitly selects InteractionPermission and CarryPermission;
PlayerCarrier.AnchorPath points to `../Presentation/CarryAnchor`.
Changing these reference policies is starter work; use the reusable
[interaction](../../gameplay/interaction/README.md) or
[carry](../../gameplay/carry/README.md) guide only when their boundary changes.

Custom replacement: author a saved game-owned host with a direct NetworkObject,
select it as OnlineGameplayWorld.PlayerScene, supply an explicit game spawn-data
provider, and add only required input/simulation/permissions. Never recursively
give the represented peer root authority or copy the starter GAS component to
an actor with different semantics.

Validation: [custom lifecycle runner](../../sandbox/shell/run_custom_player_lifecycle_probe.ps1)
covers default/custom scenes and rounds; [GAS probe](../../sandbox/gas/godot_gas_interop_probe.tscn)
covers composed-player requests, vitality/equipment and cleanup; physical replay
is checked by [motion probe](../../sandbox/netfox/player_vital_motion_probe.tscn).
Follow runtime protocols before running processes.
Current acceptance → [current state](../../docs/current-state.md).
