# Default reference composition

`shell/online_gameplay.tscn` composes the reusable OnlineGameplayShell and
OnlineGameplayWorld with `StarterRoundContent`, `StarterPlayerSpawnDataProvider`
and `player/network_player_3d.tscn`. The player composes Netfox movement,
interaction and carry permissions, inventory/equipment, GAS/vitals and revive.
Round content spawns the sample switch and carryable cube. It is a complete
working default, not a required superclass or gameplay model for consumers.

| Path | Reference responsibility |
| --- | --- |
| [player guide](player/README.md) | NetworkPlayer3D, input, simulation, camera and presentation |
| [GAS guide](gameplay/gas/README.md) | Health, Stamina, Sprint, Dash, Fortify, Downed, Dead, Respawn and their replicated player projections |
| [PlayerInventory](gameplay/inventory/PlayerInventory.cs) | One-slot storage and equipment semantics (see below) |
| [interaction examples](gameplay/interaction/) | GAS permission, instant revive and sample switch; reusable [interaction contract](../gameplay/interaction/README.md) |
| [carry examples](gameplay/carry/) | GAS permission and inventory-aware CarryableItem cube; reusable [carry contract](../gameplay/carry/README.md) |
| [shell composition](shell/online_gameplay.tscn) | Default round content, spawn data, gameplay scene and configured GameShell autoload scene; reusable [shell contract](../shell/README.md) |

GodotGAS interop in GodotGasAdapter/NetworkGasComponent is specific to this
starter's vitality and equipment model; it is not a generic factory GAS API.
Reusable contracts remain under `factory/gameplay`, `factory/networking` and
`factory/shell`. See their scoped guides when replacing a mechanism, and
[the ownership migration](../docs/v0.2-ownership-migration.md) when updating
existing scenes, namespaces or scripts.

The GodotGAS registries and generated tags are in `gameplay/gas/resources/`.
All five addon resource paths are explicitly configured in `project.godot`;
the addon has no root-directory requirement. Games choose their own GAS data
paths and semantics. Root Maaack/Steam/Netfox autoload and input settings remain
shared project integration.

## Inventory, interaction and item reference semantics

PlayerInventory is specifically coupled to NetworkPlayer3D, CarryableItem and
NetworkGasComponent. Owner input actions `store_item`, `retrieve_item`,
`equip_item`, `unequip_item` send reliable no-payload requests to the server;
server sender/owner and canonical vitality checks remain decisive. It publishes
stored/equipped NetworkObjectId scalars on the root through ordinary replication.
The cube retains its permanent runtime identity through
World → Carried → Stored → Equipped → Stored → Carried. Storage/equipment hides
the world item and does not transfer authority. The reference equipped cube
adds/removes a source-owned GAS speed capability.

Store transitions the item before the carrier forgets it; retrieve transitions
before the carrier adopts it. Downed releases a held item, ordinary death
preserves stored/equipped items, and player despawn releases hidden items to
World and removes their equipment effects. Inventory exits before GAS so source
cleanup can reach the live ASC. These are sample rules a game may replace;
GameFactory's reusable carry is only World/Carried with neutral lifecycle release.
For inventory-only work read this section and affected inventory/item sources;
load GAS or carry guides only when changing that boundary.

StarterPlayerInteractionPermission checks canonical GAS on server and projected
vitals locally. PlayerReviveInteractable owns instant revive; InteractableSwitch
demonstrates ordinary IsOn replication and local visuals. StarterPlayerCarryPermission
owns sample pickup/drop vitality gates; CarryableItem adds the storage/equipment
states absent from the reusable CarryTarget. Neither example is a hidden
requirement for custom actors/items.

## Shell and replacement map

StarterRoundContent explicitly spawns the saved sample switch/cube; its reset
clears local reference state after world cleanup. StarterPlayerSpawnDataProvider
supplies `spawn_position` for the reference player's pre-tree initialization.
The default gameplay scene selects these nodes with explicit paths and PlayerScene.
Replace only those game-owned composition roles with game content/providers,
keeping the reusable shell/world lifecycle. Root Maaack scene routing and
GameShell fallback are shared integration, not a requirement to keep starter content.

Validation examples: [round composition runner](../sandbox/shell/round_composition/run_round_composition_probe.ps1),
[carry runner](../sandbox/carry/run_carry_boundary_probe.ps1) and
[GAS/player probe](../sandbox/gas/godot_gas_interop_probe.tscn) exercise reference
compatibility. Current acceptance → [current state](../docs/current-state.md).
