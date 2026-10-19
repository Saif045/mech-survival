# Field parts

`MechPartId` identifies an actual runtime part. `NetworkObjectId` identifies its
loose world embodiment. Pickup/drop preserves both IDs and the server's exact
`PartInstance` reference. Service transfers that same instance between the body
and its temporary loose embodiment.

The nine resources in `game/mech/definitions/` are canonical model definitions
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

Normal online_gameplay.tscn points RoundContentPath to PrototypeRunContent.
There is no free debug loot in a normal run. Loose parts come from successful
salvage or player detach/replacement. Historical focused probes explicitly
spawn their own fixture parts. See [run loop](run-loop.md).

## Field service

Tab toggles the owner-only Mech Status / Service panel without pausing the
multiplayer game. All nine rows display installed model, condition and effective
physical contribution/capabilities, or EMPTY. Row buttons explicitly select a
target slot, including independent arms/legs. E picks up and Q drops as before.
A compact status list remains while closed, including carried model/condition.

When carrying a LooseMechPart, the panel shows its compatible slots and compares
CURRENT / CARRIED / CHANGE for the selected slot. MechPartReadout formats the
existing immutable MechBodyView/InstalledPart and condition-weighted definition
contributions. It does not predict native GAS modifiers or cache another mech
state. Primitive modules observe View/Changed for missing parts and condition;
functional colors remain independent of the dedicated player identity marker.

INSTALL / REPLACE sends only a selected slot to the dedicated RequestInstallRpc.
It requires a currently carried compatible loose part; missing carry is rejected,
never interpreted as detach. DETACH uses RequestDetachRpc and deliberately
removes the installed instance, leaving any carried part unchanged. Empty detach
rejects. J/K and the old carry-dependent service request are removed.

Both intents validate live server body registration, slot, authority and actual
sender against represented ownership. Installation derives a unique carried
LooseMechPart from the world's holder state, checks world/ownership and validates
its exact authoritative PartInstance. Clients supply no part ID, condition or
transform. Replacement emits the outgoing exact instance as a loose object and
despawns the incoming embodiment, retaining MechPartId and condition. Detach
also retains exact identity. No inventory or held-item registry is introduced.

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
stored inventory, wrecks/dead bodies, throw physics, persistence, loot tables,
economy, new hazards, final UI/animation/meshes.

## Validation

The current focused run-loop acceptance exercises explicit install rejection
without carry, explicit detach with a carried part, exact arm identity through
detach/reinstall, remote empty/present View observations, ownership guards and
exact improved reward installation with no resource refill. Pure product tests
cover intent selection and condition-weighted comparison/readout formatting.
Use the build/product/-RunLoop offline/two-peer commands in [run loop](run-loop.md).
Historical focused modes retain their own fixture parts and explicit API calls;
this pass does not reaccept every historical scenario.
