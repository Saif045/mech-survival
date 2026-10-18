# Networked Mech GAS foundation

Each `mech_body.tscn` owns one `MechGasComponent`, whose adapter/bridge constructs
the real vendored GodotGAS `AbilitySystemComponent` with a mech-only AttributeSet.
Task 03 defined Mobility, PowerCapacity and CoolingCapacity; Task 04 extends it
with [native Energy/Heat resources](energy-heat.md). It does not attach
the player-specific factory GAS component or import Health, Stamina, movement,
Downed/Dead or respawn semantics.

## Ownership and reconciliation

Physical identities, slots, condition, damage, composition and raw/effective
output remain in the accepted structure model. Server GAS consumes that model's
effective output and functioning capability availability. GAS owns final gameplay
attributes, tags and effects; clients consume server GAS results, not canonical
values independently derived from their physical view. Mech GAS stays outside
Netfox rollback and is not connected to movement.

The component subscribes once to `NetworkMechBody.Changed`. An immutable seven-field
baseline input aggregates three effective stats and four capability booleans.
Equivalent inputs do nothing, including no-op damage, same-value SetCondition and
parts whose installation does not change physical output/capability availability.

The bridge owns exactly one infinite native ADD GameplayEffect for the structural
baseline and its `Capability.Mobility`, `.Sensor`, `.Power`, `.Cooling` tags.
Reconciliation removes/replaces only that effect handle; unrelated effects remain
active. One or two functioning sources grant one aggregate structural tag, not
one tag per physical part. Other native effects retain their own tag references.

This choice follows inspection of the pinned addon: AttributeData's base setter
resets current value without reevaluating effects. Instead the owned additive
effect is removed/reapplied through GAS, and the component captures only the final
transaction result. Attributes use native unclamped delta math during this
transaction so intermediate removal cannot discard another additive modifier.
Published numeric values must be finite/nonnegative. No future clamp or resource
policy is introduced. The addon records fixed applied deltas; its MULTIPLY/OVERRIDE
operations are not a continuously reevaluated modifier graph. This task proves
independent native additive-effect coexistence; it does not add a generic evaluator
or promise future multiplicative gameplay semantics.

## Canonical snapshots and mirrors

All direct C#/GDScript addon calls are confined to `MechGasAdapter` and
`mech_gas_bridge.gd`. Probe/game consumers use the typed component. Capture reads
the actual ASC attributes and `has_tag_exact` results, never substitutes physical
output. Changes to native attributes/effects/tags mark a dirty flag; server-side
capture publishes changed results once. Structural reconciliation captures its
completed result synchronously. The server never mirrors/replays its own snapshot.

`MechGasSnapshot` is an immutable, validated aggregate. `NetworkMechBody.GasState`
is a single spawn/on-change replicated JSON property using the existing
ReplicationComponent/MultiplayerSynchronizer. Values and tag flags (extended with
Task 04 resources) travel atomically, separately from physical `Composition`.

Client bodies construct a real local ASC, apply only the authoritative GAS snapshot
through native instant attribute overrides and tag reference updates, then capture
the mirror to verify equality. No client baseline reconciliation, effects or timers
run authoritatively. Private snapshot setters/internal server-guarded reconciliation,
effect and publication APIs reject client mutation; no arbitrary attribute/tag
setter or client request RPC is exposed.

Body reset reconciles an empty structural baseline; it does not erase unrelated
gameplay effects. Despawn/disconnect removes the whole GAS composition, its owned
effect handles/tags, mirror and cached snapshot. A new body owns a fresh ASC.

## Development proof and debug display

`game/probes/gas/mech_debug_modifier.gd` is a development-only infinite native
GameplayEffect adding Mobility 0.5 and `Development.MechModifier`. The adapter
loads it only when the probe's development control is invoked. This is not a game
mechanic. Its server-only apply/remove path proves that final canonical Mobility
can differ from physical output and that damage refresh preserves the independent
effect. Client mirrors receive the final modified value without running that effect.

The existing scrollable probe displays structural output and canonical/mirrored
GAS attributes/capabilities, plus native ASC/development-effect diagnostics.
Offline acceptance exercises all physical mutation paths, two-source tag
aggregation, Sensor destruction/replacement, Power/Cooling updates, no-op churn,
modifier coexistence/removal, reset and destruction with an active effect.

The existing two-peer scenario compares actual native ASC readbacks at twelve
stages: damaged+modified late sync; damage with modifier active; modifier removal;
all capability sources installed; partial/destroying damage and Sensor tag loss;
last Mobility source destroyed; replacement restores tags; detach; SetCondition;
reset; fresh pristine body; fresh independent modifier. Each stage rejects twelve
client physical/GAS mutation attempts. Destruction clears native state with the
development effect still active. Test RPCs carry expectations/acknowledgements;
actual GAS results travel only through ordinary canonical replication.

Use the existing build/product/foundation test commands and
`game/probes/run_mech_probe.ps1 -Mode Offline|Multiplayer -RunId <unique_id>`.
Runs freeze source/build, use a disposable APPDATA profile with command-level
permission when needed, preserve evidence, restore APPDATA and delete the profile,
verify zero owned processes/objects and release port 24873. No Steam/VM matrix or
normal-profile investigation is needed.

## Accepted evidence

- Build passed with zero warnings/errors. Product suite: 50 passed (41 body/damage
  cases plus 9 pure GAS-input/snapshot cases); foundation suite: 73 passed.
  Commands used `--disable-build-servers -m:1 -p:UseSharedCompilation=false`.
  Unit logs: `game/artifacts/mech_body/mech_gas_units_20261012_034951/`.
- Offline **PASS**: `mech_gas_teardown_offline_20261012_035827`, PID 26520.
  The real vendored ASC handled all three attributes, native capability tags,
  aggregate Mobility, Sensor destruction/replacement, all structural mutation
  paths, additive modifier preservation/removal and equivalent-input no-churn.
  Native attributes, tags, effects and cached state were cleared on destruction.
- Multiplayer **PASS**: `mech_gas_matcher_multiplayer_20261012_040302`, host PID
  29320/client PID 31776. Twelve canonical/mirror readbacks matched, with 144
  rejected client operations. Late synchronization received structural Mobility
  near 0.35 and actual GAS Mobility 0.85. Damage with the modifier still active
  changed GAS to 0.75; removing it restored the current physical baseline near
  0.25. Sensor/aggregate Mobility tag loss and restoration, Power/Cooling updates,
  reset, fresh ASC state and destruction with the development effect active passed.
- Both runs preserved source/build hashes, removed temporary profiles, restored
  APPDATA and verified zero owned processes/objects. Multiplayer port 24873 was
  released. No engine/runtime errors occurred. Managed assembly SHA-256:
  `D4AB08C0E7CF640C8B9D2A014FCA9AF97B0D3D51E979F8BD4CBB832ACAC251C4`.
  Evidence is under `game/artifacts/mech_body/<run_id>/`: contract, frozen hashes,
  stdout/stderr/engine logs and terminal result. No VM/Steam acceptance was needed.
- Earlier multiplayer attempts remain preserved as **FAIL**:
  `mech_gas_multiplayer_20261012_035040` exposed probe peer detachment before awaited
  world cleanup; `mech_gas_teardown_multiplayer_20261012_035848` exposed a runner
  regex counting only single-digit stages. User-approved isolated fixes changed
  only probe teardown ordering and the stage matcher (`\d` to `\d+`). GAS/runtime,
  GameFactory, Netfox and addon behavior stayed unchanged across those fixes.
  Teardown now removes world/GAS state while the peer is still assigned and quits
  immediately after closing it, with no frame wait for an already empty world.
- PowerShell syntax and staged/unstaged `git diff --check` passed.

This accepts the real-GodotGAS mech composition on the focused local two-peer
boundary. Task 04 should consume these canonical values and add explicit resource
policies rather than duplicate physical or GAS state.

## Later consumers

Task 04's resource arithmetic is documented separately. Movement consumption, repair, salvage,
hazards, core death/rescue, persistence, combat, final UI/VFX/audio remain absent.
Physical condition stays in structure; current resources belong to the ASC.
