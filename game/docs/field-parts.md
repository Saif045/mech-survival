# Field parts

`MechPartId` identifies an actual runtime part. `NetworkObjectId` identifies its
loose world embodiment. Pickup/drop preserves both IDs and the server's exact
`PartInstance` reference. Installation is not implemented.

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

Exclusions: installation/detachment input, replacement, repair, loose condition
changes, per-part charge, inventory/stored/equipped states, wrecks, salvage,
throw physics, persistence, loot tables, economy and final presentation.

Validation uses the existing product test project and playable probe:

```powershell
dotnet build GameFactory.sln --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test game/tests/MechSurvival.Tests/MechSurvival.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
./game/probes/run_playable_mech_probe.ps1 -FieldParts -Mode Offline -RunId <unique_id>
./game/probes/run_playable_mech_probe.ps1 -FieldParts -Mode Multiplayer -RunId <unique_id>
git diff --check
```

The focused mode starts the host's round and picks up a leg before the client
connects, proving existing spawn and held-state observation. After reset, the
second round includes both players and exercises client interaction/drop requests.
It checks canonical body/GAS/player composition as a narrow regression. Evidence,
frozen hashes and cleanup results live in `game/artifacts/playable_mech/<run_id>/`.
This local ENet probe makes no Steam/VM or manual visual acceptance claim.
