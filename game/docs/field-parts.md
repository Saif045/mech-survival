# Field parts

`MechPartId` identifies an actual runtime part. `NetworkObjectId` identifies its
loose world embodiment. Pickup/drop preserves both IDs and the server's exact
`PartInstance` reference. Service transfers that same instance between the body
and its temporary loose embodiment.

The seven resources in `game/mech/definitions/` are canonical model definitions
shared by bodies and loose parts. Each definition freezes once into immutable
configuration. `catalog.tres` supplies the small model lookup for prototype
content and loose projection validation; it stores no runtime items.

`LooseMechPart.Spawn(world, part, transform)` is server-only and accepts an
existing uninstalled instance. NetworkWorld applies spawn data before tree entry,
then binds that exact instance through its server-only configure callback.
Binding validates identity, canonical definition, condition and uninstalled state.
`GetServerPart()` rejects clients and unbound nodes. No runtime ID injection
constructor was added to PartInstance.

All peers receive an immutable `LoosePartProjection`: GUID serialized as a string,
model and finite condition in [0,1]. Spawn data also contains a finite Godot
Transform3D, applied to both root and CarryTarget.WorldTransform before tree entry.
The scene composes a normal server-owned NetworkObject, cheap mesh/label,
StaticBody3D collision and the released factory CarryTarget. No rigid-body physics
or separate item/holder registry exists.

PlayableMech directly composes factory PlayerInteractor and PlayerCarrier with
an explicit game-owned MechFieldPermission. This currently permits interaction,
pickup and drop because no incapacitation policy exists. Factory components retain
sender, target, range, holder and capacity validation. Default semantic `interact`
and `drop_item` input stays enabled. Presentation/CarryAnchor follows the existing
presentation hierarchy; carry state and actions remain outside rollback.

Normal online_gameplay.tscn points RoundContentPath to FieldPartsRoundContent.
Each round spawns a condition-0.8 debug leg and pristine debug power part near
the players. NetworkWorld removes them on round reset; new rounds create fresh
part and object identities. The content node retains no round state.

## Field service

The game-owned MechServiceController on PlayableMech reads semantic actions outside
rollback: `service_next_slot` (J) cycles the nine explicit MechSlot values;
`service_part` (K) services the selected slot. A local-owner-only label displays
the selection and prototype controls. Selection stays local and unreplicated.
E picks up nearby parts; Q drops the carried part as before.

With no carried loose mech part, service detaches the selected installed instance
and creates a new LooseMechPart beside the body. An empty slot is a rejected no-op.
With a carried compatible part, service installs it into the selected empty slot
or replaces its current part. The outgoing exact instance becomes a new loose,
interactable object. The incoming loose network embodiment despawns. Its
MechPartId, definition, condition and server object reference survive installation;
its temporary NetworkObjectId ends. A later detach creates a fresh embodiment ID.

The owner-to-server reliable request contains only the intended MechSlot. The
server validates role, represented owner/sender, enum, live world registration
and body authority. It resolves a unique carried LooseMechPart from that world's
existing holder state, checks object ownership/availability, obtains the actual
server PartInstance and validates its immutable projection and body definition.
It trusts no client-provided part/object ID, condition, definition or transform.
Client calls to another mech's owner entry are ignored; the RPC independently
rejects forged ownership. No new inventory or held-item registry exists.

MechBody.CanReplacePart is the non-mutating compatibility/installation-claim
policy for both empty and occupied slots. CanAttachPart additionally requires an
empty slot. NetworkMechBody checks configured definitions and server authority;
its server-only internal accessor supplies installed references to the service
orchestrator. Mutable body rules remain inside NetworkMechBody.

After complete validation, installation releases CarryTarget, reconciles the
carrier via ForgetHeldItem, replaces through NetworkMechBody, spawns the outgoing
part if any, then despawns the incoming embodiment. Invalid compatibility leaves
body, carry and world state unchanged. If detach spawning throws, recovery
reinstalls the returned exact instance. If outgoing replacement spawning throws,
recovery restores the previous exact body instance and reattaches the incoming
part through PlayerCarrier.TryPickup. Unexpected failures remain errors rather
than silently losing either part; no transaction framework is introduced.

Body changes flow through the existing structural projection and native Mech GAS.
The service controller calculates no Mobility or other stats. Two pristine legs
produce Mobility 2 / speed 6; detaching one gives 1 / 3; reinstalling restores
2 / 6; substituting the condition-0.8 field leg gives 1.8 / 5.4. Leg service does
not reset Energy or Heat. Existing capacity clamping/no-refill behavior remains
owned by GAS/resources; service never initializes resources.

Exclusions: repair, loose-part damage/condition changes, per-power-module charge,
stored/equipped inventory, wrecks/dead bodies, salvage/hacking, throw physics,
persistence, loot tables, economy, hazards, extraction, final UI/animation/meshes.

## Validation

Validation uses the existing product test project and playable probe:

```powershell
dotnet build GameFactory.sln --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test game/tests/MechSurvival.Tests/MechSurvival.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
./game/probes/run_playable_mech_probe.ps1 -Service -Mode Offline -RunId <unique_id>
./game/probes/run_playable_mech_probe.ps1 -Service -Mode Multiplayer -RunId <unique_id>
git diff --check
```

The retained `-FieldParts` mode starts the host's round and picks up a leg before the client
connects, proving existing spawn and held-state observation. After reset, the
second round includes both players and exercises client interaction/drop requests.
It checks canonical body/GAS/player composition as a narrow regression. Evidence,
frozen hashes and cleanup results live in `game/artifacts/playable_mech/<run_id>/`.
This local ENet probe makes no Steam/VM or manual visual acceptance claim.


The `-Service` mode exercises both owners' detach/reinstall/replacement requests,
exact instance and ID transfer, incompatible/empty/invalid/forged rejection,
body/GAS/loose-state convergence, carrier reconciliation and outgoing interaction.
It measures 24-tick locomotion at 6, 3, restored 6 and 5.4 speed with the existing
movement tolerance, plus client-owner/server-observed speed 3. Probe-only resource
simulation pause preserves controlled Energy 6 / Heat 12 observations. Real
production resource cadence remains enabled. Two rounds check cleanup and fresh
identities, with the same disposable profiles and bounded runner.
