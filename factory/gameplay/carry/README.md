# Server-authoritative carry

This is the canonical carry mechanism guide. GameFactory owns one-item holder
identity, validated requests, authoritative attach/release/drop and derived
held presentation. Games own eligibility and lifecycle meaning. A custom
player/item needs no NetworkPlayer3D, NetworkGasComponent, starter inventory
or Presentation/CarryAnchor hierarchy. Existing paths have not moved.

## Scope

Use carry for a server-owned loose world object attached to a represented
actor, with all peers deriving its held transform from an explicit anchor.
Use the interaction boundary for pickup and ordinary replication for discrete
state. Do not use this as inventory, equipment, physics grabbing, joint-based
multiplayer rigid-body simulation, installed sockets or persistent ownership.
It is intentionally one-item capacity and a 3D composition.

## Public boundaries

| Type/API | Responsibility |
|---|---|
| ICarryTarget | NetworkObject, HolderNetworkObjectId, IsAvailable, server TryAttach(holder)/TryRelease(holder, transform) |
| ICarryHolder | NetworkObject, CarryAnchor, HasCarriedItem, server TryPickup(ICarryTarget) |
| ICarryPermission | CanPickup(NetworkObject actor), CanDrop(NetworkObject actor); canonical on server, side-effect-free local hint |
| PlayerCarrier | One-item cache, explicit dependencies, owner RequestDrop(), validated reliable RPC |
| PlayerCarrier.ReleaseHeldItem(reason) | Trusted server lifecycle release, bypassing input permission but retaining holder/item checks |
| PlayerCarrier.ForgetHeldItem/AdoptHeldItem | Trusted server cache reconciliation after game-owned transitions; not inventory operations |
| CarryTarget | Optional reusable direct-child component implementing ICarryTarget and IInteractable; only World/Carried states |
| CarryCapabilities.Target/Holder | Root capability wins, otherwise one direct implementing child; ambiguous direct capabilities throw |
| StarterPlayerCarryPermission | Reference-player GAS policy |
| CarryableItem | Reference cube adapter retaining World/Carried/Stored/Equipped, cube presentation and inventory compatibility |

Capability lookup is restricted to a supplied network object's host/direct
children. It is not recursive, named-component lookup or a global registry.

## Holder composition

```text
GameActor (Node3D)
  NetworkObject                  # normal factory composition
  Model
    ToolSocket (Node3D)           # any authored name/hierarchy
  Permissions (Node, ICarryPermission + optional IInteractionPermission)
  Use (PlayerInteractor)
  GripController (PlayerCarrier)
```

Set GripController.AnchorPath to `../Model/ToolSocket` and PermissionPath to
`../Permissions`. Both paths are relative to the carrier. The actor must have
the usual direct NetworkObject. Empty/missing/wrong-type dependencies report
configuration errors and do not become permissive. Author dependencies before
tree entry; retain the configured policy/anchor for the carrier's lifetime.

An actor without vitality explicitly composes a policy such as:

```csharp
public partial class CarryPermission : Node, ICarryPermission
{
    public bool CanPickup(NetworkObject actor) => true;
    public bool CanDrop(NetworkObject actor) => true;
}
```

A game with restrictions checks its own canonical state on the server and
local replicated projections for input suppression. The carry policy has no
target rules, GAS tags, Downed/Dead, health or mech state in its contract.
Interaction still needs its separate Task A IInteractionPermission policy.

Set EnableDefaultInput=false for game-owned input and call RequestDrop() on
the local owner's carrier. Default input retains `drop_item`; pickup is chosen
by PlayerInteractor's optional `interact`/nearest-target convenience or the
game calling RequestInteraction(target.Id). InputMap names are unchanged.

## Target composition

```text
LoosePart (Node3D; game-owned mesh/collision, no CarryableItem base class)
  NetworkObject
  PartMesh (MeshInstance3D)
  ContactBody (StaticBody3D)
    ContactShape (CollisionShape3D)
  CarryCapability (CarryTarget)
```

Set CarryCapability.CollisionPath to `../ContactBody/ContactShape`. The shape
must sit under an appropriate Godot CollisionObject3D for physical collision.
This component creates
its own normal MultiplayerSynchronizer with an explicit configuration for its
HolderNetworkObjectId and WorldTransform properties. It does not change
ReplicationComponent or require carry fields on a player/item root. Do not
duplicate replication of those same values or continuously replicate the held
host transform. Mesh/collision shapes and any game-specific availability remain
game-owned; CarryTarget supports a single CollisionShape3D.

Set WorldTransform explicitly in the authored scene or from the host's normal
INetworkSpawnInitializable implementation before tree entry, for example:

```csharp
public void ApplyNetworkSpawnData(Variant data)
{
    GetNode<CarryTarget>("CarryCapability").WorldTransform =
        data.AsGodotDictionary()["spawn_transform"].AsTransform3D();
}
```

The component applies that transform in World state. HolderNetworkObjectId=0
means World; a positive value means Carried. There are no Stored/Equipped,
ability-grant or item-database requirements. An installed/game-specific item
can own root IInteractable availability rules (root dispatch has priority), or
implement ICarryTarget directly when its state/presentation needs differ.
Custom implementations must preserve the same server-only transition contract.

## Authoritative request flow and security

1. The local owner selects a target NetworkObjectId through Task A interaction.
2. The server resolves it in NetworkWorld and validates sender/represented
   ownership, actor interaction permission, self-target, range and IInteractable.
3. CarryTarget.CanInteract checks availability and holder capacity. Local checks
   are hints; server checks decide. Interact invokes the holder's TryPickup.
4. PlayerCarrier checks explicit carry permission, one-item capacity, registered
   same-world target, non-self identity, server root authority and unheld state.
   It calls TryAttach and records the permanent item's runtime ID.
5. RequestDrop sends a reliable no-payload RPC. The server validates positive
   sender, represented-owner equality, canonical drop permission and held state.
6. The server computes `actor.GlobalTransform` plus basis-transformed DropOffset
   (default `(0, 0.5, -1.25)`), verifies exact holder identity and releases.

Clients provide neither drop transform, holder ID, item state nor NodePath in
requests. No authority transfers occur. OwnerPeerId identifies the actor's
represented connection; it is not the carried item's holder identity. Holder
IDs are runtime NetworkObjectId values resolved through the same NetworkWorld,
not PeerIds, PlayerIds, Steam IDs or durable identities.

TryPickup/TryAttach/TryRelease/ReleaseHeldItem and reconciliation methods are
trusted server APIs, not extra RPCs. A game-owned caller is responsible for its
semantic/range checks when invoking server pickup directly instead of interaction.
Targets must return false on attach while unavailable/already held and on release
by a different holder. A custom target must remain server-owned.

## Replication and presentation

The reusable target replicates holder ID and world/drop transform with ordinary
on-change synchronization. Every peer resolves the holder capability and copies
its explicit CarryAnchor.GlobalTransform locally while carried. The collider is
disabled while held; local interactable-group membership follows carry state.
Missing holder replicas are retried each frame, accommodating spawn ordering.
The target retains its NetworkObjectId across pickup/drop and is never recreated.

For the starter Netfox player the anchor is under interpolated Presentation.
For a custom actor it can be anywhere appropriate for its visuals. Anchor/follow
transforms remain presentation outside rollback. Carry does not own Netfox time,
movement, jumping, prediction or history. No carry state joins rollback.

## Lifecycle and starter compatibility

ReleaseHeldItem(reason) is a server-only escape from owner input policy for
game lifecycle cleanup. It still resolves the recorded item and validates holder
identity; no Downed/Dead meaning exists inside the reusable carrier. The carrier
also attempts release on exit. If the target was removed, drop safely clears its
stale cache; the next pickup also discards a removed/queued target from capacity.
Normal round cleanup should still remove players before remaining world objects.

The default player explicitly wires StarterPlayerCarryPermission and the old
Presentation/CarryAnchor as authored configuration. Canonical server GAS and
local vitality projections preserve default gates. NetworkGasComponent calls
neutral ReleaseHeldItem when entering Downed. CarryableItem implements the
carry capability while retaining its reference-only storage/equipment transitions
and existing root replication/presentation. It resolves holder capability without
requiring a child named PlayerCarrier.

PlayerInventory only replaces its cache-clear/set calls with ForgetHeldItem and
AdoptHeldItem and uses HasCarriedItem for capacity. Store still transitions first,
then forgets; retrieve transitions first, then adopts. These operations do not
grant a universal inventory API. Death persistence, source effects, cube speed
capability and inventory-before-GAS exit ordering remain starter behavior.

Migration: every carrier scene must select explicit AnchorPath and PermissionPath.
Use ICarryTarget instead of CarryableItem in reusable pickup callers. Replace
DropOnDowned with game-owned lifecycle calls to ReleaseHeldItem. Old internal
ClearStoredItem/SetRetrievedItem/CanReceiveRetrievedItem helpers are replaced by
neutral reconciliation/capacity APIs. There are no path moves or action renames.
Carrier diagnostics no longer include starter health/vital state or emit a
special Downed event. Generic dropped reasons and the starter's GAS lifecycle
events provide that correlation; the starter supplies the Downed reason string.

## Validation and common mistakes

After building, run `factory/sandbox/carry/run_carry_boundary_probe.ps1 -Label <unique>`.
It imports saved scenes; checks default GAS/vital/inventory/equipment behavior;
then runs isolated custom and default host/client ENet pairs. It asserts pickup,
drop, holder replication, local/remote follow, default movement/jump while held,
one-item capacity, wrong sender/non-holder, server-only pickup/drop denial,
stale removed item and holder/world cleanup. Required configuration failures are
also checked. The runner preserves assembly hash, source inputs, logs and result
under `factory/artifacts/carry_boundary/<Label>/`, restores environment and verifies
process/profile cleanup. Timeouts are bounded; no Steam session/VM is needed.

Common mistakes: using PeerId as holder; moving authority to the holder;
replicating held transforms continuously; putting carry in Netfox history;
silently allowing missing policies; leaving hidden inventory objects in World
availability; forgetting before transitioning; using a client transform for drop;
assuming scene node names define capabilities; removing nodes without Free.

Non-goals: inventory/equipment sockets, install/replace rules, mass/hands,
containers, item database, generic actions/events, transport abstraction, scene
framework, arbitrary rigid-body pickup physics or physical directory relocation.

Current acceptance/evidence → [current state](../../docs/current-state.md#task-b-detailed-acceptance-record).
