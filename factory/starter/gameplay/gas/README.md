# Starter GAS interop and game composition pattern

This is a reference for composing GodotGAS with authoritative networking.
**Do not inherit or attach starter NetworkGasComponent merely because a game
wants to use GodotGAS.** It requires the starter NetworkPlayer3D and its specific
vitality, movement, inventory/equipment and UI projections. GodotGasAdapter is
also a starter semantic adapter, not a generic GameFactory GAS API.

## Ownership

| Owner | Contract |
| --- | --- |
| GodotGAS | Attributes/AttributeSets, GameplayEffects, abilities, tags and ASC mechanics |
| Reusable GameFactory mechanisms | Network identity, authority, ordinary replication and lifecycle |
| Starter | Health/Stamina, Sprint/Dash/Fortify, self damage, Downed/Dead/Respawn/revive, cube equipment effects and specific adapters/projections |
| Game | Actual attributes, effects, abilities, tags, resources, permissions and lifecycle meaning |

Raw addon behavior belongs in [GodotGAS README](../../../../addons/GodotGAS/README.md)
and [AbilitySystemComponent source](../../../../addons/GodotGAS/components/ability_system_component.gd).
Do not copy its API manual here or add game rules to reusable networking.

## Canonical state and explicit projections

The reusable architectural pattern is:

```text
game-specific canonical GAS on server
  → explicit ordinary projections
    → client mirrors/read-only projections
      → gameplay consumers and presentation
canonical GAS
  → explicit physical projection
    → Netfox physical simulation
```

Server permission and effect/ability execution use the canonical ASC.
Client projections can suppress input and render UI; they never authorize
damage, equipment, revival or capability grants. Validate RPC sender, represented
owner and game permission before activation, including stale input after
incapacitation. Do not infer Downed/Dead from projected health.

The starter [NetworkGasComponent](NetworkGasComponent.cs) creates a real ASC
via `GodotGasAdapter.Create(this, Multiplayer.IsServer())`. On server it
captures GasSnapshot and publishes host properties; it also publishes speed,
dash authorization and respawn boundaries. [NetworkPlayer3D](../../player/NetworkPlayer3D.cs)
exposes [Replicated] properties consumed by the normal ReplicationComponent.
Clients subscribe to Synchronized/DeltaSynchronized and apply changed projections.
`GodotGasAdapter.ApplySnapshot` mirrors Health, Stamina and Downed/Dead into
the non-authoritative bridge; fortify/cooldowns/speed and other values remain
explicit host projections. This is not replication of arbitrary ASC internals,
effect instances or every attribute. Host never reapplies its own client mirror.

## Attributes, effects, tags and abilities

In the reference, [health_attribute_set.gd](health_attribute_set.gd) defines
Health, Stamina and MoveSpeed (health/stamina clamped to 0–100); addon modifiers/effects own
canonical changes. Abilities such as [fortify](fortify_ability.gd) and
[dash](dash_ability.gd) define costs, cooldowns and grants. Tags express canonical
capabilities/vital state, not client-derived health thresholds.
[godot_gas_health_bridge.gd](godot_gas_health_bridge.gd) wires ASC lifecycle,
sprint drain/regeneration, vitality and explicit snapshot mirroring.

For a new game attribute, author its AttributeSet/resources and effect/ability
semantics under `game/`, choose which scalar/tag-derived values actually need
replication, and add those to the game's projection host or explicit synchronizer.
Adding an addon attribute does not automatically add a network field.
A mirror must be treated as read-only by gameplay code even if the adapter
internally mutates its local ASC to apply the snapshot.

## Source-owned effects

Capabilities supplied by one equipped item must be removable without erasing
effects from other items, buffs or progression. The starter attaches an
infinite effect to its actual item source:

```gdscript
# Extract from the starter bridge; _asc is the live AbilitySystemComponent.
var context := GameplayEffectContext.new(source_node)
var spec := GameplayEffectSpec.new(effect, context)
return _asc.apply_effect_spec(spec) != null
# When that equipment source ends:
_asc.remove_effects_from_source(source_node)
```

The effect grants the reference EquipmentCube tag and speed modifier; the starter
supports one equipped slot and denies duplicate cube capability. This is not
a universal equipment API. In a game refreshing one source, remove/reapply only
that source's effects, retain the correct source lifetime, and republish the
resulting projections. Do not clear all effects or remove by a shared broad
tag when unrelated sources may carry it. If one source node owns multiple
capabilities, source removal removes that source's effects together; games needing
finer granularity must choose their ownership/handles accordingly.

The starter vitality reset uses an explicit transient-tag allowlist and preserves
equipment effects. Ordinary death preserves stored/equipped items and their
capabilities; player despawn releases hidden items to World and removes their
equipment source. A game chooses its own death/reset persistence policy.

## Resources and C#↔GDScript boundary

The starter scene [godot_gas_component.tscn](godot_gas_component.tscn) contains
the ASC and health bridge. Registries/generated tags live in [resources](resources/).
All five root project settings explicitly select starter locations:

| project.godot section [godot_gas] key | Starter value |
| --- | --- |
| resources/attributes/draft_configuration_file | res://factory/starter/gameplay/gas/resources/attributes_draft.cfg |
| resources/attributes/output_directory | res://factory/starter/gameplay/gas/resources/attributes/ |
| resources/cues/registry_file | res://factory/starter/gameplay/gas/resources/cue_registry.tres |
| resources/tags/registry_file | res://factory/starter/gameplay/gas/resources/tag_registry.tres |
| resources/tags/generated_script | res://factory/starter/gameplay/gas/resources/gameplay_tags.gd |

The draft/output locations are configured authoring targets; not all are committed
starter resources. Games choose their own `res://game/...` locations and
reconcile settings/generated scripts/autoloads together. The addon does not
require a root godot_gas directory.

Keep C#↔GDScript calls concentrated in a game-owned typed adapter. The reference
[GodotGasAdapter](GodotGasAdapter.cs) centralizes string method names, Variant
conversions and snapshot access. Its methods (`ApplyFortify`, `ApplyDash`,
`ApplyEquipmentCubeCapability`, `TryRevive`, etc.) intentionally describe this
starter, not a promised generic API. GDScript owns addon-domain integration;
C# owns validated network/lifecycle coordination. Do not scatter raw Call/Get
strings across request handlers or copy starter strings into unrelated games.

## GAS and Netfox

GAS itself **does not enter rollback history**. Do not serialize ASC nodes,
active effects, tags, Health or Stamina into RollbackSynchronizer state.
Do not re-execute costs, grants, RPCs or cleanup during resimulation.

The starter projects GasMoveSpeed and incapacitation for physical consumers.
Dash uses ordinary GasDashAuthorizationRevision with physical dash duration,
direction and last consumed authorization in history. Respawn uses ordinary
RespawnRevision/RespawnTick; the simulation consumes a newer revision at/after
its tick and resets physical position/velocity/grounded/dash state. Only
`Simulation:last_respawn_revision` records reset consumption in history, so
restoring an earlier snapshot replays the boundary. This is not ASC rollback
or a process-frame teleport. Inputs are ignored at that reset tick.

For a game's different GAS-derived physical rule, define the explicit
projection and its tick/revision/replay semantics; an arbitrary live attribute
read is not automatically deterministic history. Use the
[Netfox integration contract](../../../networking/netfox/README.md) only when
changing this boundary, and the actual
[starter simulation](../../player/network_player_3d_simulation.gd) for its behavior.

## Minimal custom-game composition and lifecycle

```text
GameActor (game script; ordinary replicated projection properties)
  NetworkObject                 # factory prefab / server-owned identity
  GameGasCoordinator            # validated requests, publish/apply projections
    GameASC                     # GodotGAS scene with game AttributeSet/abilities
      GameGasBridge             # game GDScript addon integration
  GameInventory                 # optional source owner, exits before live ASC
  Input / Simulation / Presentation  # optional physical composition
```

These Game* nodes are game-authored roles, not existing GameFactory types.
Start with a saved game scene and spawn-data contract from networking/shell.
Create a typed adapter around that ASC, server-only ability execution and explicit
replicated projections; a direct Godot synchronizer is also valid when projection
properties are not on the root. Compose permissions against canonical server
state and client hints. Add only the abilities/resources the game needs.

Pair replication/ASC event subscriptions with teardown. Remove source effects
while ASC/source are still live, then free nodes. The reference player authors
NetworkGasComponent before PlayerInventory: reverse child exit order lets
inventory remove equipment capability before GAS exits. Do not rely on garbage
collection or hidden inventory objects remaining after player despawn.

## Working example, mistakes and validation

Reference composition → [player guide](../../player/README.md) and
[starter map](../../README.md). Common mistakes: attaching NetworkGasComponent
to an arbitrary actor; assuming GasSnapshot is generic ASC serialization;
client canonical activation; health-derived vital tags; global effect clearing;
missing source cleanup; addon-generated paths left pointed at starter; GAS
mutation during replay; and assuming every snapshot field becomes a mirrored tag.

After build, the real-addon/composed-player probe is
[godot_gas_interop_probe.tscn](../../../sandbox/gas/godot_gas_interop_probe.tscn);
the physical replay probe is
[player_vital_motion_probe.tscn](../../../sandbox/netfox/player_vital_motion_probe.tscn).
Carry/round boundary runners also run starter compatibility smoke. Follow
[testing protocol](../../../docs/testing-protocol.md) for execution.
Current accepted GAS and consumer evidence → [current state](../../../docs/current-state.md).
A generic GAS network framework, universal attribute schema and game-specific
ability catalog are non-goals.
