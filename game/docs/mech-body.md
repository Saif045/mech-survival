# Modular mech body foundation

## Model and boundaries

A mech body is a server-owned `NetworkWorld` object, separate from a connected
player. It has nine explicit logical slots; empty slots are valid. No player/body
possession or automatic core-required rule is introduced.

| Slot | Current meaning |
| --- | --- |
| Core | Central mechanical module; no vitality rule yet |
| Torso | Structural body module |
| Sensor | Sensing module; no sensor gameplay yet |
| LeftArm / RightArm | Independently installed arm modules |
| LeftLeg / RightLeg | Independently installed mobility modules |
| Power | Power capacity module; no consumption or energy simulation |
| Cooling | Cooling capacity module; no heat simulation |

`MechPartDefinitionResource` is Godot inspector configuration: model key,
compatible slots, capabilities and base contributions. On startup it becomes an
immutable `PartDefinition`. The small debug definitions are local subresources
in `mech_body.tscn`; this is not an item database. Slot/capability integer arrays
use the enum values in `MechSlot.cs` and are validated when frozen. Keep model
keys and enum meanings consistent across peers; normal matched builds supply
the same definitions.

`PartInstance` is a server runtime object with a newly generated `MechPartId`
(GUID), immutable definition, current condition and nullable installed slot.
Its installed-body claim prevents one instance from occupying two bodies.
Identity survives detach/reinstall/replace and is replicated as data. No caller
can assign a runtime part's ID. This is session identity, not a persistent item
key and not a reuse of Steam ID, PeerId, PlayerId or NetworkObjectId. Parts have
no independent world nodes in this slice; the body has its own NetworkObjectId.

`MechBody` supplies pure C# attachment policy. `NetworkMechBody` keeps that
mutable model private, checks server peer 1 authority and world binding before
every mutation/creation, and exposes immutable observations. Client gameplay
cannot mutate the canonical model. There are no installation-request RPCs.

## Operations and condition

Attach succeeds only for an empty compatible slot and an uninstalled part.
Duplicate installation, occupied slots and incompatibility return `false`.
Replace validates first, returns `false` without removing anything on rejection,
or installs the new instance and returns the detached previous instance. Replace
on an empty compatible slot succeeds with a null previous part. Replacing with
the already installed instance rejects. Empty detach returns null every time.
Other slots are untouched. Detached parts retain their ID and condition and can
be installed into another body configured with equivalent definitions.

Condition is a finite float in `[0, 1]`: zero is destroyed, one is pristine.
Out-of-range values, NaN and infinities throw before changing state. Setting
valid condition on an empty slot returns `false`. Destroyed installed parts stay
installed until explicitly detached. No automatic repairs or replacement exist.

`View.GetPart(slot)` and `View.HasPart(slot)` inspect composition.
`InstalledPart.Functioning` means condition is greater than zero; it is only a
query classification, not a movement or damage effect. To inspect two legs,
check both explicit leg slots and their `Functioning` fields.
`CountCapability` defaults to functioning parts and can also count all installed
parts. `SumContribution` sums raw base properties of all installed parts,
including destroyed ones, in slot order. Each part's own contributions are also
available. Task 02 adds distinct effective structural queries: base contribution
multiplied by condition, per part and for the body. `SumContribution` keeps its
raw meaning. These are physical projections, not gameplay attributes. Previously
captured views remain immutable. See [the damage contract](mech-damage.md).

## Replication and cleanup

The existing GameFactory `ReplicationComponent` / `MultiplayerSynchronizer`
replicates one `Composition` JSON string on spawn and on change. It contains
slot, runtime part ID, definition model key and condition. One atomic property
avoids half-applied detach/replace or assignment/condition updates. Definitions,
presentation and speculative gameplay effects are not sent as mutable state.
Every received projection is completely validated before replacing `View`;
duplicate slots/IDs, invalid compatibility/condition/identity and unknown models
reject. Composition never enters rollback history. This bounded nine-slot body
does not need an independent transport, event bus or per-part replication node.

`ResetBody` detaches everything. On tree exit the body releases all installation
claims and clears its stored projection. The probe clears local world objects on
server disconnect. Normal product session lifecycle integration and operated-body
assignment remain future work; this task does not modify the framework shell.

## Probe and validation

Open `game/probes/mech_body_probe.tscn` directly in Godot. Offline it spawns an
empty authoritative body. Labels show all slots, short debug IDs, condition,
functioning status and base contributions. Select a slot, model and condition;
buttons attach, replace, detach, set condition, reset, and spawn after an empty
world. Select a leg model with Sensor to inspect compatibility rejection.
Clients can press controls and see the server-authority rejection.

For a local interactive pair, launch this scene directly in two Godot processes,
host first, using `-- --mech-host` and `-- --mech-client`. Client transport is
loopback ENet port 24873. This probe requests no Steam lobby/session and launches
no VM; existing project autoload/native initialization remains unchanged.

```powershell
dotnet build GameFactory.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test game/tests/MechSurvival.Tests/MechSurvival.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test tests/GameFactory.Tests/GameFactory.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
./game/probes/run_mech_probe.ps1 -Mode Import -RunId <unique_import_id>
./game/probes/run_mech_probe.ps1 -Mode Offline -RunId <unique_offline_id>
./game/probes/run_mech_probe.ps1 -Mode Multiplayer -RunId <unique_multiplayer_id>
git diff --check -- GameFactory.csproj game
```

The runner follows the repository's focused local multiprocess probe pattern
and runtime protocol: unique preserved evidence, executable/build/port preflight,
test-owned processes only, host readiness before client launch, bounded waits,
terminal result and verified cleanup. Artifacts are in
`artifacts/mech_body/<run_id>/`. Runtime assertion RPCs only select/acknowledge
expected observations; the composition actually travels through ordinary
GameFactory replication. The source/build/configuration stays frozen throughout
each attempt. Agent-launched runs use a fresh disposable `APPDATA` profile;
the runner restores the environment and removes the validated profile even
after failures. Request command-level permission when engine/process access
requires leaving the sandbox; no permanent full-access mode or project/runtime
workaround is needed. Use normal profile access only when a specific test
reproduces the interactive desktop environment. No Steam A/B matrix or export
is needed for this contract.

The current scripted path extends body/damage acceptance with canonical GAS
checks; [the GAS contract](mech-gas.md) describes its twelve stages and guards.
The damage contract preserves Task 02's eight-stage evidence. The Task 01 results below preserve the original six-stage
acceptance evidence. Offline checks also
exercise incompatible/duplicate installation, cross-body transfer, replacement
return value, destroyed queries, empty detach and invalid condition rejection.

## Validation and acceptance evidence

- C# build: passed, zero warnings/errors.
- Product xUnit rules: 20 passed; engine-independent linked product source.
- Existing GameFactory xUnit tests: 73 passed.
- Normal-profile Godot import: **PASS**, run `mech_direct_20261012_023457_import`
  with unrestricted execution, no runtime error markers, unchanged source/build,
  and zero owned processes after cleanup. Earlier restricted-agent startup
  failures are classified as agent-environment failures, not project defects.
- Offline runtime: **PASS**, run `mech_offline_guard_20261012_024557`, PID 29168.
  The actual Godot scene, frozen configuration resources, NetworkWorld spawn,
  compatibility/duplicate checks, cross-body transfer, replacement return,
  destroyed-state queries, detach, condition bounds and reset all completed.
- Multiplayer replication: **PASS**, run `mech_multiplayer_20261012_024607`,
  host PID 18416, client PID 23060. Two real local Godot/ENet processes matched
  all six snapshots and all 42 client mutation rejections. Late join preserved
  the part ID; replacement changed it deterministically; condition 0 remained
  installed. Detach/reset cleared the slot consistently. Disconnect removed
  client body state; server despawn cleared the old body and a fresh body was
  empty. These assertions test existing GameFactory spawner/synchronizer behavior
  above the transport, with no Steam A/B matrix or manual visual acceptance claim.
- Both passed runs recorded unchanged source/build hashes, zero world objects at
  teardown, zero live owned processes, temporary profile removal and restored
  APPDATA. No VM participant was used. The managed runtime SHA-256 was
  `4510AADE0AC786126A24BD0EF292EB31A7AD8BC1209F9F8A67D6C0318BBED195`.
  Preserved artifacts are under `artifacts/mech_body/<run_id>/` and include the
  contract, frozen hash snapshots, stdout/stderr/engine logs and terminal result.
- Earlier offline attempt `mech_offline_20261012_024436` is preserved as **FAIL**:
  rules passed but probe teardown tried unsubscribing signals offline mode never
  subscribed. After the user authorized the separate narrow fix, teardown was
  guarded by actual subscription state, then rebuilt and accepted in the runs
  above. This probe-only fix did not change project/runtime startup configuration.

The modular body contract is accepted on this scripted runtime/multiplayer
boundary. Later features must validate their own behavior without casually
reopening the accepted foundation.

## Extension expectations and exclusions

Add/remove explicit slot enum values and update authored compatibility resources,
probe labels where needed, and tests together. Do not assume these nine slots are
the final game schema. Add focused capabilities/base properties only when a real
consumer requires them. Keep authoritative state server-owned and local visuals
outside that state. Preserve GameFactory release ancestry and upstream ownership.

Damage effects, repair, energy drain, batteries consuming power, heat, cooling
simulation, movement degradation, mines, corrosion, sand, parasites, rarity,
hacking, salvage, persistent inventory, rescue, extraction, run timers,
progression, procedural generation, combat and final art are intentionally absent.
