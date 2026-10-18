# Networking contracts

Use these mechanisms for server-owned dynamic scenes, session players and
ordinary authoritative state. They use Godot MultiplayerApi, MultiplayerSpawner
and MultiplayerSynchronizer directly. They do not supply durable accounts,
game rules, automatic client authority or a universal gameplay base class.

## Identities

| Value | Meaning / lifetime | Use |
| --- | --- | --- |
| SteamUserId | Platform/account identity | Steam adapter and optional diagnostic enrichment |
| PeerId | Live Godot connection; server is 1 | RPC sender and transport ownership checks |
| PlayerId | Server-allocated player identity in a PlayerLifecycle lifetime | PlayerRegistry; distinct from connection and object |
| NetworkObjectId | Runtime object identity allocated by one NetworkWorld | Target/holder lookup and spawn/despawn |
| NetworkObject.OwnerPeerId | Represented connection metadata on a bound object | Input ownership and validated request policy |

IDs are positive typed values in `GameFactory.Networking.Peers`, `.Players`
and `.Objects`; Steam IDs live in `GameFactory.Steam.Models`. None of the three
network IDs is a persistent save/account key. Reconnection and new rounds create
new player/object identities. The live world's object allocator must remain
monotonic during retained-peer reuse. Steam details belong in [Steam](../steam/README.md).

## Scene composition

Instance [network_object.tscn](objects/network_object.tscn) as a direct child
named `NetworkObject` of any game-owned host (Node, Node3D or CharacterBody3D):

```text
GameDoor (Node3D, game script)
  NetworkObject                 # NetworkObject script
    ReplicationComponent
      MultiplayerSynchronizer
    AuthorityComponent
  Visuals / Collision           # game-owned
```

The prefab includes authority and ordinary replication components.
`NetworkObjectComponent` subclasses must be direct children of NetworkObject;
they register on entry, initialize at NetworkObject ready, and shut down on exit.
`GetComponent<T>` requires exactly one matching component; `TryGetComponent<T>`
allows absence but still rejects ambiguity. These are focused capabilities, not
a recursive service locator. `Host`, `World`, `Id`, `OwnerPeerId` and `IsBound`
describe the bound scene; ID/owner/world access before binding is invalid.
Do not add an unbound network prefab directly to the live tree.

## Authority and replication

`AuthorityComponent` / `INetworkAuthority` reports the host's Godot authority
through `AuthorityPeerId` and `HasAuthority`; it does not assign authority.
Ordinary roots retain default server authority. Never recursively assign
OwnerPeerId to the root: a represented client owns input, not shared simulation.
The [Netfox guide](netfox/README.md) owns the special input/simulation topology.

`ReplicationComponent` / `INetworkReplication` builds its synchronizer config
from host C# properties annotated with `[Replicated]`. Properties must also be
Godot-accessible (export them on a custom host). Default mode is OnChange and
Spawn is true; Always and Never are explicit alternatives. No child-property
scan is performed. The component exposes `Synchronized` and `DeltaSynchronized`
events for applying derived presentation/mirrors.

```csharp
using Godot;
using GameFactory.Networking.Objects.Components.Replication;

public partial class GameDoor : Node3D
{
    [Export, Replicated(ReplicationMode.OnChange)]
    public bool IsOpen { get; set; } // mutate only after server validation
}
```

Replication converges state; it does not deliver every intermediate action.
For discrete commands use a direct reliable RPC on a consistently addressed
node: accept requests only on the server, derive sender from
`Multiplayer.GetRemoteSenderId()`, validate represented owner and canonical
game permission, resolve targets in the world, then mutate. Never trust a
client-supplied owner, transform or NodePath. Host-local calls need an explicit
trusted server path because remote sender is not meaningful outside an RPC.
[Interaction](../gameplay/interaction/README.md) is the working reusable example.
Ordinary replication and these RPCs are outside rollback history.

## Spawn and despawn

Instance [network_world.tscn](world/network_world.tscn), normally under the
OnlineGameplayWorld. NetworkWorld creates Players and WorldObjects spawn groups,
each with a Godot MultiplayerSpawner. The host scene's `NetworkObject.SpawnGroup`
selects the group; default is WorldObjects. Only the server calls Spawn/Despawn.
The scene must be a saved PackedScene with a valid resource UID present on both
peers. A live object keeps its ID until despawn; pickup/drop does not respawn it.

```csharp
// Server, with an already entered NetworkWorld named world:
var scene = GD.Load<PackedScene>("res://game/items/game_part.tscn");
var data = new Godot.Collections.Dictionary { ["position"] = Vector3.Zero };
var part = world.Spawn<Node3D>(scene, PeerId.Server, data);
var id = part.GetNode<NetworkObject>("NetworkObject").Id;
// Later on server: world.Despawn(id);
```

The snippet needs `Godot`, `GameFactory.Networking.Objects` and
`GameFactory.Networking.Peers` imports; its game scene is consumer-authored.
The host implements `INetworkSpawnInitializable.ApplyNetworkSpawnData(Variant)`
when it needs initialization. NetworkWorld sends prefab UID, object ID,
represented owner and spawn data through its spawner; it binds identity, applies
data on every peer, then invokes the optional server-only configure callback,
all before tree entry. Put replica-required values in spawn data, not just that
callback. Any non-Nil data on a host without the interface is a configuration error.
Game/starter owns the schema; `spawn_position` is not a universal factory field.
Only Nil means no spawn data; even an empty dictionary requires the interface.

For that game-owned `position` schema, the saved scene's root can be:

```csharp
using Godot;
using GameFactory.Networking.Objects;

public partial class GamePart : Node3D, INetworkSpawnInitializable
{
    public void ApplyNetworkSpawnData(Variant data) =>
        Position = data.AsGodotDictionary()["position"].AsVector3();
}
```

It still needs the direct NetworkObject prefab; initialization runs before
`_Ready()`, so it must not depend on ready-time caches or the entered scene tree.

`TryGet(id, out object)`, `Objects` and `Count` inspect the local registry.
Despawn queues the host for freeing; unregister occurs on tree exit, so callers
must wait for removal during cleanup. Clients observe server despawns.

## Player lifecycle

`PeerRegistry` tracks local/remote connections; `PlayerRegistry` maps players to
peer and object IDs. `PlayerLifecycle(peers, players, runtime, spawn, despawn)`
subscribes to peer changes and processes peers already present. Server roles
allocate PlayerId, invoke the supplied spawn callback, then register a
NetworkPlayer. DedicatedServer excludes its local peer; clients never create
authoritative players. Disconnect invokes despawn and removes the player entry
even if the callback throws. Dispose unsubscribes; it is not bulk world cleanup.

Use [OnlineGameplayWorld](../shell/README.md) to compose this lifecycle with
PlayerScene and an explicit spawn-data provider. Games own player contents and
initialization policy; no NetworkPlayer3D inheritance is required. Replace
individual game components/providers rather than copy world lifecycle.

## Examples and validation

- Reference player: [starter/player](../starter/player/README.md).
- Minimal non-player-base composition and retained round cleanup:
  [custom lifecycle runner](../sandbox/shell/run_custom_player_lifecycle_probe.ps1).
- Round/spawn-data integration: [round composition runner](../sandbox/shell/round_composition/run_round_composition_probe.ps1).
- Pure identity/registry/role policy: `factory/tests/GameFactory.Tests/`.

Build before source probes and follow [testing protocol](../docs/testing-protocol.md)
for process runs. Current acceptance → [current state](../docs/current-state.md).
Common mistakes are static/unbound network instances, client spawns, missing
UIDs, replicated fields placed on children, server-only configure data expected
on replicas, identity reuse during a live peer, and treating owner metadata as
authority. Persistent identity, game inventory and transport abstraction are non-goals.
