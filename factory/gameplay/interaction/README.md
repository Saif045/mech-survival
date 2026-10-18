# Server-authoritative interaction

Interaction handles a discrete request to use one live target. Games own input,
actor permissions and target semantics; no GAS, inventory or player-base dependency
exists. Use it for switches, doors and pickup entry points. It is not continuous
physics grabbing, aiming/line of sight, inventory or an ability framework.

## Contract and minimal actor

Public types live in `GameFactory.Gameplay.Interaction`:

| Type/API | Responsibility |
| --- | --- |
| PlayerInteractor | Optional default input, owner request and server validation |
| RequestInteraction(NetworkObjectId) | Custom input/selection entry point |
| IInteractionPermission.CanRequestInteraction(NetworkObject, PeerId) | Game-owned actor-state permission |
| IInteractable.CanInteract(InteractionContext) | Side-effect-free target eligibility |
| IInteractable.Interact(InteractionContext) | Target mutation after server validation |
| InteractionContext | RequestingPeerId, Player, Target, ServerPlayerPosition |

```text
GameActor (Node3D, any game script)
  NetworkObject            # instance factory/networking/objects/network_object.tscn
  InteractionPermission    # explicit IInteractionPermission node
  Use (PlayerInteractor)   # PermissionPath = ../InteractionPermission
```

Spawn the actor through NetworkWorld with its represented OwnerPeerId. A direct
child named NetworkObject is required; a Node3D host supplies range position.
PermissionPath is relative to the interactor. Empty, absent or wrong-type paths
throw a configuration error; there is no implicit permissive fallback.

```csharp
using Godot;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;

public partial class InteractionPermission : Node, IInteractionPermission
{
    public bool CanRequestInteraction(NetworkObject actor, PeerId peer) => true;
}
```

This is an explicit no-restrictions actor. A restricted game policy reads
canonical actor state on the server and a local projection for input hints.
It must support both roles, perform no mutation and contain no target rules.

## Minimal target and custom input

```csharp
using Godot;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects.Components.Replication;

public partial class GameSwitch : Node3D, IInteractable
{
    [Export, Replicated] public bool IsOn { get; set; }
    public override void _Ready() => AddToGroup("interactable");
    public bool CanInteract(InteractionContext context) => true;
    public void Interact(InteractionContext context) => IsOn = !IsOn;
}
```

Save this Node3D with the normal NetworkObject prefab as its direct child;
spawn it on the server through NetworkWorld. ReplicationComponent synchronizes
IsOn and the game derives local visuals. Target resolution prefers a host
implementing IInteractable, otherwise its first direct implementing child in
scene order. It does not recurse. Avoid ambiguous duplicate capabilities.

Default `EnableDefaultInput=true` reacts to `interact` for the local represented
owner and selects the nearest eligible host in group `interactable` within
`InteractionRange` (default 2.75). Only bound direct NetworkObject targets count.
For custom targeting set `EnableDefaultInput=false`, then call:

```csharp
// On the local owner's actor, after ready; selected is a bound NetworkObject:
GetNode<PlayerInteractor>("Use").RequestInteraction(selected.Id);
```

Custom selection need not use the group or default input action. Both routes
use the same reliable request and server checks. You can compose CarryTarget
for pickup; its distinct contract lives in [carry](../carry/README.md).

## Trust, authority and identity

The client sends only target ID, as a long at the RPC boundary. The server
derives a positive remote PeerId, checks equality with the actor OwnerPeerId,
checks actor permission, rejects self-target, resolves NetworkObjectId in the
actor's NetworkWorld, requires an interactable Node3D target, checks 3D origin
distance, calls server CanInteract, then dispatches Interact. Host-local owners
enter the same validation with PeerId.Server. Account ID and PlayerId do not
cross this request boundary. No authority is transferred.

Local CanInteract and permission checks only hint availability. The misleadingly
named `InteractionContext.ServerPlayerPosition` is local during selection and
authoritative only during server dispatch. Never trust that local context for
mutation. Interact is a trusted callback, not a second public RPC; game code
calling it directly must supply equivalent authorization. Interaction does not
add line-of-sight, cooldown or installed-item policy; games implement those rules.
Discrete target state uses ordinary replication, outside Netfox history.

## Reference, mistakes and validation

Working reference: [starter player scene](../../starter/player/network_player_3d.tscn)
and [starter switch](../../starter/gameplay/interaction/InteractableSwitch.cs).
StarterPlayerInteractionPermission and PlayerReviveInteractable own the sample
GAS/vital semantics; custom actors need neither.

Common mistakes: omitting PermissionPath, treating local hints as authorization,
mutating in CanInteract, sending transforms/NodePaths, placing capabilities deep
under visuals, assuming selection provides aim/visibility validation, or deriving
server permission from untrusted client health.

Validation: after build run
`factory/sandbox/interaction/run_interaction_boundary_probe.ps1 -Label <unique>`
under [testing protocol](../../docs/testing-protocol.md). It covers custom actor
and starter compatibility, host/client owners, target/range/self/sender rejection,
canonical permission and invalid configuration, with bounded local ENet pairs.
Current acceptance/evidence → [current state](../../docs/current-state.md).
