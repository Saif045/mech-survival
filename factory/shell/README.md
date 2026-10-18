# Online shell and round composition

This is the canonical detailed shell/world guide. GameFactory owns online
lifecycle and network mechanisms; games own content and player initialization.
The working reference composition lives under `factory/starter/`.
No copied world lifecycle, level framework or second content registry is needed.

## Ownership

| Owner | Responsibilities |
|---|---|
| GameShell/Maaack glue | Host/Join intent and configured gameplay-scene loading, menu/pause integration |
| OnlineGameplayShell | One SteamSession; lobby readiness/revisions; round phase; start/return acknowledgements; discovery visibility; terminal Leave |
| OnlineGameplayWorld | RuntimeContext, PeerRegistry, PlayerRegistry, PlayerLifecycle, existing NetworkWorld; authoritative player creation/disconnect; start/reset/cleanup |
| Game/starter scene | PlayerScene, initialization data, dynamic round content, static level visuals/collision and gameplay rules |
| Content/provider capabilities | Narrow callbacks and data creation; no lifecycle/session/identity ownership |

Normal Host, Join, invite and command-line connect use Maaack's configured
gameplay scene via GameShell. Set `maaacks_game_template/game_scene_path` to
the saved game-owned scene. Preserve the normal OnlineGameplayShell root,
its OnlineGameplayWorld child, NetworkWorld child and PauseMenuController
composition. The shell consumes one launch intent and retains its session
while individual rounds are removed/recreated. Static authored level nodes can
live beside NetworkWorld without becoming network objects automatically.

## Public composition contracts

OnlineGameplayWorld exports PlayerScene, RoundContentPath and
SpawnDataProviderPath. Paths are relative to the world node, not scene root.

```csharp
public interface IRoundContent
{
    void StartRound(NetworkWorld world);
    void ResetRound();
}

public interface IPlayerSpawnDataProvider
{
    Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId playerId);
}
```

RoundContentPath is optional. Empty explicitly means players-only content;
there is no automatic starter fallback even if a content node exists elsewhere.
A nonempty missing/wrong-type path reports a configuration error. The spawn
provider is required on every peer: empty/missing/wrong-type paths fail clearly.
Select dependencies before tree entry and keep them alive for the world lifetime.
No global/recursive type lookup or service locator is involved.

StartRound receives only the existing world and is invoked only on the server,
after authoritative players have spawned. Use it for dynamic non-player objects.
ResetRound clears small local state/subscriptions on every peer once per started
round, after normal world-clear. It also runs on terminal Stop, where network
objects intentionally remain alive until peer teardown. It must tolerate the
client role (where StartRound was never invoked) and partial start, and must not
spawn/despawn objects, clear registries/IDs or replace session/lifecycle policy.
Prefer synchronous, idempotent local cleanup; failures propagate to the shell's
existing failure path instead of reopening a partially cleared round.

The spawn provider is invoked only on the server for each PlayerLifecycle
spawn. It receives NetworkPeer and PlayerId, returns a non-null supported Godot
Dictionary, and does not spawn, allocate IDs or change authority. The world
passes this data to NetworkWorld.Spawn with the peer's represented OwnerPeerId.
Only the network object identity returned by that spawn enters PlayerRegistry.

## PlayerScene and initialization

PlayerScene must be a saved PackedScene with valid UID and the normal direct
child named NetworkObject. Its root can be any Node supported by NetworkWorld;
NetworkPlayer3D/Node3D inheritance is not required by this lifecycle seam.
Because a dictionary is supplied, implement INetworkSpawnInitializable on the
root. ApplyNetworkSpawnData receives server-created data before tree entry on
both the authoritative instance and client replicas. Configure initialization
there, without adding the root to a tree or changing its NetworkObject binding.

The data schema belongs to the configured player/provider pair. It can include
position, loadout key, resources or other supported Variant values. Neither
`spawn_position` nor any particular gameplay field is mandated by the world.
Do not put client-authored initialization, live runtime Object references or
authority manipulation in the provider. Review game-specific dictionaries as
part of its player contract.

## Start, reset and retained-session reuse

Start is the world entry used by the shell after readiness checks. On server:
validate ready composition/empty state; register connected peers; create
PlayerLifecycle with world-owned spawn/despawn delegates; create players from
PlayerScene/provider data; then invoke optional content. Client Start sets
client role only; it does not create players or content.

ResetRoundAsync preserves the accepted ordering:

1. Mark round stopped; snapshot authoritative player NetworkObjectIds.
2. Remove peers while PlayerLifecycle is still subscribed, causing player despawns.
3. Wait until all snapshotted player IDs disappear. Player exits may release items
   or remove game-owned effects while non-player objects still exist.
4. Server despawns remaining dynamic NetworkWorld objects.
5. All peers wait for their world to become empty. Clients observe spawner
   despawns; they do not free authoritative objects themselves.
6. Dispose lifecycle, reset local content state, verify IsEmpty and report clear.

World-clear waits are bounded to ten seconds. OnlineGameplayShell separately
waits for connected clients' revision-matched clear acknowledgements before
reopening Lobby. Round failures terminate through its existing failure behavior.
Repeated rounds retain the same world and connected peer. Stop is terminal
local cleanup, not round reset: it unsubscribes lifecycle/content while leaving
replicated nodes for Steam peer closure and eventual scene destruction. A
partially started/reset world still receives local Stop cleanup.

## Identity, authority and dependencies

Steam/account ID, transient PeerId, PlayerId and runtime NetworkObjectId are
distinct. OwnerPeerId is represented-owner metadata, not root Godot authority.
Clients never authoritatively spawn players. Roots/state remain server-owned;
any input authority split belongs to the configured player composition.

NetworkWorld allocates fresh monotonically increasing object IDs across rounds
while retained. Do not replace/reinitialize the world or reset its allocator.
Do not clear Netfox identities/history servers or manually restart NetworkTime
for round reuse. The provider cannot allocate NetworkObjectId; PlayerLifecycle
owns its player records and supplies PlayerId. Treat those records/IDs according
to their current lifecycle, not as durable account/progression identity.

SteamSession belongs to the shell and retains the lobby/native peer through
round return. Netfox observes the Godot peer lifecycle and supplies movement
rollback only for compositions opting into it. These callbacks add no transport
or rollback framework. For changes to those internals, route through their
scoped AGENTS guides rather than loading Steam investigation history or Netfox
internals for ordinary content work.

## Explicit starter/default composition

```text
OnlineGameplayShell
  OnlineGameplayWorld
    RoundContent (StarterRoundContent)
    PlayerSpawnDataProvider (StarterPlayerSpawnDataProvider)
    NetworkWorld
    Floor / environment / lights
  PauseMenuController
```

The default scene explicitly selects both capabilities. StarterRoundContent
spawns the switch at `(0, 0.625, -1.5)` and cube at `(0, 0.35, 1.5)`.
StarterPlayerSpawnDataProvider returns `spawn_position`: player 1 `(-2,2,0)`;
other players `(2,2,0)`. The default player and its interaction/carry/inventory/
GAS composition remain unchanged. These implementations are starter/reference
content, not requirements of reusable lifecycle. See the
[starter guide](../starter/README.md) and [ownership migration](../docs/v0.2-ownership-migration.md).

## Minimal game-owned composition

```text
GameOnlineScene (OnlineGameplayShell)
  OnlineGameplayWorld
    NetworkWorld
    GameContent (IRoundContent)
    SpawnRules (IPlayerSpawnDataProvider)
    game-owned static level
  PauseMenuController
```

Set PlayerScene to the game-owned player, RoundContentPath=`GameContent` and
SpawnDataProviderPath=`SpawnRules`. Set RoundContentPath empty for a players-only
round without replacing lifecycle code. For example:

```csharp
public partial class GameContent : Node, IRoundContent
{
    [Export] public PackedScene BeaconScene { get; set; } = null!;
    public void StartRound(NetworkWorld world) => world.Spawn<Node>(
        BeaconScene, PeerId.Server,
        new Godot.Collections.Dictionary { ["kind"] = "beacon" });
    public void ResetRound() { /* clear local counters/subscriptions only */ }
}

public partial class SpawnRules : Node, IPlayerSpawnDataProvider
{
    public Godot.Collections.Dictionary CreateSpawnData(NetworkPeer peer, PlayerId id) => new()
    {
        ["spawn_position"] = new Vector3(id.Value == 1 ? -6 : 6, 3, 4),
        ["loadout"] = peer.IsLocal ? "scout" : "engineer"
    };
}
```

The beacon/player root must consume its corresponding dictionary through
INetworkSpawnInitializable. Dynamic content appears automatically on clients
through the existing spawner and participates in world teardown. A content
implementation may maintain a small counter/subscription, not a second object
registry or authoritative player lifecycle.

## Migration, common mistakes and validation

Existing world scenes must configure SpawnDataProviderPath. Move SwitchScene/
CarryableScene values from OnlineGameplayWorld onto an explicit
StarterRoundContent node if retaining those examples; those world exports are
removed. Configure RoundContentPath to that node, or empty for no content.
Default `online_gameplay.tscn` is migrated. Existing custom players must consume
their provider's schema; the old implicit spawn_position schema now belongs to
the explicit starter provider. No InputMap, shell RPC, Steam/Netfox vendor,
authority or physical-path migration is introduced.

Common mistakes: spawning clients locally; returning a null dictionary;
providers spawning players; content clearing the world/allocator; selecting
missing paths and assuming fallback; duplicating static/authored network objects;
resetting content before players release their state; freeing client replicas;
forgetting subscriptions; using Stop as a substitute for ResetRoundAsync.

Run `factory/sandbox/shell/round_composition/run_round_composition_probe.ps1 -Label <unique>`
after building. The bounded disposable-profile suite imports fixtures, checks
starter GAS/vital/inventory smoke and existing three-round/default custom-player
lifecycle probes, then runs custom and empty-content host/client pairs. It checks
custom beacon/crate content, additional player data before tree entry, correct
identities/authority, no starter leaks, player-first teardown, reset callbacks,
fresh identities in the second round, retained peer and full cleanup. It also
checks explicit configuration failures and terminal local content cleanup.
Logs, tested input/assembly hashes and terminal result are preserved under
`factory/artifacts/round_composition/<Label>/`; cleanup verifies no owned processes or
disposable profiles remain. This does not claim new Steam/VM A/B evidence.

Non-goals: level/game-mode/objective/encounter frameworks, content database,
generic loader/DI/event bus, new spawn registry, transport/session protocol,
movement/rollback changes, gameplay subsystem redesign or physical relocation.

Current acceptance/evidence → [current state](../docs/current-state.md#task-c-detailed-acceptance-record).

## Application gameplay fallback composition

GameShell resolves Maaack's valid `game_scene_path` for every online entry route.
If that target is absent or invalid, it uses exported `FallbackGameplayScenePath`.
The starter autoload scene sets that property to its moved default gameplay scene.
A game composing GameShell must select a valid configured target or fallback;
missing both is a clear configuration error. The former public
`OnlineGameplayScenePath` constant was removed to keep reusable shell code free
of starter resource dependencies. Existing main-menu, join, pause and options
integration remains here; default gameplay and GameShell scene composition live
in `factory/starter/shell/`. See the ownership migration for resource/namespace
updates. Round-content and spawn-data contracts are unchanged by this move.
