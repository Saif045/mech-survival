# Architecture

This is the canonical overview. It distinguishes proven implementation,
implemented-but-not-yet-accepted slices, and planned work.

Physical placement is canonical in [ownership](ownership.md), with the v0.2
move/API map in [migration notes](v0.2-ownership-migration.md). Mechanisms cannot
depend on starter or game types/resources. Starter and games compose mechanisms;
starter GAS, player and inventory are reference implementations, not required APIs.

## Current dependency shape

```text
Steam and Steam-gameplay probes
└── SteamSession -> ISteamAdapter -> GodotSteamAdapter -> GDScript bridge
    -> GodotSteam / SteamMultiplayerPeer -> Godot MultiplayerApi
        ├── PeerRegistry / PlayerLifecycle / PlayerRegistry
        ├── NetworkWorld -> NetworkObject -> authority and replication components
        └── gameplay RPC, MultiplayerSpawner, and MultiplayerSynchronizer
```

Steam/Godot `MultiplayerPeer` is the accepted online session path. There is no generic transport or generic session coordinator: neither had a second concrete use and both duplicated the actual Steam/Godot lifecycle. The `SteamPlatform` autoload owns the process-lifetime GodotSteam adapter and shuts Steam down once at application exit. Each `SteamSession` owns one friends-only lobby and the peer it assigns to Godot's `MultiplayerApi`; leaving a gameplay scene does not reinitialize Steam. This is Steam-specific by design.

Netfox is vendored at source pin `38f59778b02bfd1a3dedc7dcc985945d7058858d`, reporting 1.49.3 (upstream upgrade notes still say Unreleased). `NetworkEvents` observes Godot multiplayer lifecycle and remains the sole GameFactory/Netfox owner of `NetworkTime` start/stop. `NetworkWorld` spawns the players and retains their identity metadata; Netfox owns rollback-sensitive movement simulation, input/state history, prediction, and interpolation.

Input reaches the persistent `NetworkCommandServer` RPC transport and the persistent `NetworkSynchronizationServer`, `NetworkHistoryServer`, and `NetworkIdentityServer`. Rollback input remains unreliable. Snapshot decoding consumes each payload before resolving its identity; removed player identities are safely skipped rather than targeting removed per-player RPC nodes. Object paths and Netfox identity allocation remain monotonic while the peer/session is live. Identity tables must never be manually cleared during live session reuse. See [Netfox contract](../networking/netfox/README.md) and [session reuse acceptance](session-reuse-acceptance.md).

## Application shell

Maaack Game Template is the vendored GDScript application-shell dependency. It supplies maintained main/pause/options menus, persistent local settings, input remapping, loading UI, menu navigation, audio/UI helpers, credits, and local save/global-state/level patterns. `factory/shell/` is intentionally thin C# glue: bootstrap selects the normal shell or an explicit `--run` probe, the online flow composes lobby readiness and reusable rounds, and terminal Leave performs `SteamSession` teardown before Maaack returns to the menu. The project-owned options composition includes Controls, Audio, and Video only: Maaack remaps and persists the project `InputMap`; GameFactory defines the default action names; future games define action behavior. Maaack's example Game tab and currently inert sensitivity sliders are not composed.

Maaack facilities are local application/UI tools, not multiplayer authority. The online shell owns accepted lobby readiness and Lobby → Gameplay → Lobby transitions. Return to Lobby clears authoritative round objects, waits for client world-clear acknowledgements, and reopens the lobby with a new readiness revision while retaining the Steam session/peer. New rounds have fresh gameplay state and identities; terminal Leave tears down the session. Joining/rejoining is supported in Lobby; Gameplay hides the lobby and rejects new peers. Progression, win/loss, and results remain future game-owned rules. Maaack's corresponding example helpers are not composed into the networked flow.

OnlineGameplayWorld retains player/world lifecycle and player-first teardown;
game/starter composition selects optional IRoundContent and required
IPlayerSpawnDataProvider through explicit relative paths. Content uses the
existing world for server-only non-player spawns; the provider supplies data,
not spawning/authority/identity behavior. Local content cleanup follows normal
world-clear, or terminal Stop while replicated nodes remain for peer closure.
An empty content path means no objects, never starter fallback. The default
scene explicitly composes the original switch/cube and player positions through
starter implementations. Detailed contract/migration is canonical beside the
code in [shell README](../shell/README.md).

Gameplay code is not coupled to GodotSteam. `ISteamAdapter` and `GodotSteamAdapter` isolate the bridge; raw Godot networking remains usable above that boundary. The current adapter covers lifecycle, local identity, lobbies, overlay/presence operations, and Steam-ID/peer mapping. Dedicated Steam servers and Steam authentication are not implemented APIs; `RuntimeMode.DedicatedServer` remains a valid future gameplay role.

## Runtime identities and gameplay layers

`RuntimeContext` names `Offline`, `Client`, `ListenServer`, and `DedicatedServer`. `PeerId` is a positive transient Godot peer ID; `PeerRegistry` is a local view of known peers. `PlayerId` is a positive session-scoped player identity, not a Steam/account identity. `NetworkObjectId` identifies a dynamic runtime object. These identities remain separate.

`PlayerLifecycle` is engine-independent server-side orchestration over runtime, peer, and player registries plus gameplay spawn/despawn delegates. Listen servers create the local server player; dedicated servers do not. Clients never authoritatively create players.

`NetworkObject` is an open component host attached beneath a gameplay node. Default authority and replication component scenes are replaceable, discovered through capability interfaces. `ReplicationComponent` owns its `MultiplayerSynchronizer` and interprets `[Replicated]` metadata; gameplay can still use direct Godot RPC where appropriate.

`NetworkWorld` server-allocates positive IDs, owns dynamic object registration, and routes scene instantiation through generated `MultiplayerSpawner` groups. It binds object identity and represented owner peer before tree entry. Owner-peer metadata does not transfer Godot authority from the server. Static/authored object registration and persistent identity are planned.

`factory/gameplay/interaction/` is the narrow discrete-gameplay boundary:
clients send a target `NetworkObjectId`, while the server resolves it through
`NetworkWorld` and validates sender ownership, target, and server-side range.
The sample switch's ordinary replicated state intentionally stays outside
Netfox rollback and is acceptance-proven on the two-account path.

The canonical [interaction guide](../gameplay/interaction/README.md) owns
permission composition, request validation and custom input details. Starter
GAS/revive policy stays outside that reusable mechanism.

`factory/gameplay/carry/` provides player/item-independent one-item carry.
Actor permission and anchor are explicit composition; holder/target capabilities
resolve on the host/direct children. Pickup composes Task A interaction; drop
uses a validated reliable request and server-computed transform. Item authority
stays on server 1 and held presentation remains derived outside rollback.
`CarryTarget` supplies World/Carried state; the starter cube adapts its separate
Stored/Equipped rules through the same narrow capability. Lifecycle release is
neutral; the starter GAS composition owns Downed meaning. The canonical detailed
contract, scene examples and migration are in
[carry/README.md](../gameplay/carry/README.md).

`factory/starter/gameplay/inventory/` composes carry with a one-slot inventory and equipment: the permanent item's identity survives World/Carried/Stored/Equipped transitions. The server validates requests and grants/removes source-owned equipment capabilities through the GAS adapter. Death preserves stored/equipped items; player despawn releases them to World and removes their effects.

`factory/starter/gameplay/gas/` integrates real GodotGAS abilities, attributes, effects, and tags. Canonical server GAS owns gameplay capabilities and the Downed/revive/Dead/respawn lifecycle; ordinary replication publishes projections and gates gameplay requests. Netfox consumes movement permission/speed, dash authorization, and respawn revision/tick, but GAS/discrete state stays outside rollback history unless explicitly represented as deterministic projection/input. Replayable dash motion and respawn consumption belong to physical rollback state.

## Evidence and direction

GameFactory is distributed as a versioned project foundation through immutable
Git tags. Separate game repositories retain upstream ancestry and a permanent
`gamefactory` remote, and merge releases while manually reconciling shared
integration files. [ADR 0007](decisions/0007-upstream-downstream-distribution.md)
defines the boundary; the [consumer guide](consuming-gamefactory.md) covers use.
Addon/submodule packaging is deferred until real games justify a clean boundary.

The Steam-gameplay probe has both manual two-account evidence and a concrete host-PC-to-VM acceptance scenario covering lobby membership, native/Godot connection, player lifecycle, NetworkWorld spawn/despawn, server-authoritative door mutation, replicated-revision acknowledgement, and distributed diagnostics. The external harness requires two real Steam accounts and is not a generic Godot integration framework or CI coverage.

Future work must be justified by playable co-op slices. Potential areas include dedicated Steam servers when there is a real deployment need, persistent identity, Godot integration/multiprocess testing, CI, packaging, and gameplay primitives. Do not reintroduce a platform-neutral transport/session layer without a concrete second implementation that needs it.
