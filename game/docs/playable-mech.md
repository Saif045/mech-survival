# Playable mech and input contract

**Task 05 ACCEPTED on gamefactory-v0.1.2**, release commit
`adf1a8544f5ffd372e2fc51f38ac116443a59b47`. The official upstream release resolves
Host, direct Join, Steam invite Join and +connect_lobby through the same configured
Maaack gameplay scene with factory fallback. No downstream framework workaround
was applied. The earlier routing blocker and first teardown failure remain in
preserved local evidence.

PlayerLifecycle currently spawns and tracks the player's initial operated body.
Player identity remains conceptually distinct from physical body identity.
`PlayerId` identifies a session player, `PeerId` the current connection,
`NetworkObjectId` the operated runtime body and `MechPartId` an installed runtime
part. Represented `OwnerPeerId` selects input/camera ownership; the physical root
remains server-owned. Possession, persistent cores and body transfer are deferred.

## Normal composition

Maaack's `game_scene_path` selects `game/shell/online_gameplay.tscn`, an inherited
factory online shell with only `OnlineGameplayWorld.PlayerScene` overridden to
`game/player/playable_mech.tscn`. The normal session/lobby/world behavior and sample
switch/carryable remain. No sample player interaction/carry/GAS components are
attached to the mech.

The playable scene inherits the accepted `mech_body.tscn` definitions and real
Mech GAS composition. `NetworkMechBody` is now a CharacterBody3D; arbitrary base
bodies remain empty, without locomotion or automatic initialization. PlayableMech
consumes shell `spawn_position` before tree entry, uses the Players spawn group,
and on the server installs the nine existing debug parts at condition 1. It then
explicitly initializes Energy to current PowerCapacity once; fresh Heat is zero.
Production retains accepted idle/resource simulation. Movement adds no cost/load.

## Movement and authority

The existing NetfoxRollbackPlayerComponent configures root/server authority,
owner authority on Input, server authority on Simulation, and normal Netfox roots
before their initialization. Canonical composition, damage, GAS and resources
remain server-owned ordinary replication outside rollback.

The visible prefab lists are:

- State: `:position`, `:velocity`, `Simulation:grounded`.
- Input: `Input:movement` (Vector2), `Input:jump_pressed` (bool).
- Interpolation: `Presentation:position` with prediction enabled and input broadcast
  disabled. The project retains history 64.

InputMap supplies move_left/right/forward/backward and jump. Jump presses queue
until one recorded Netfox input tick. Simulation reads recorded input exclusively
in `_rollback_tick`, applies canonical GAS-derived horizontal speed, gravity 18 and jump speed 5,
and brackets move_and_slide with NetworkTime.physics_factor. Capsule collision
provides floor/environment contact. PlayableMech exposes getter-only GasMobility,
GasHasMobility and GasMoveSpeed from the canonical MechGasComponent snapshot.
The initial tunable balance is MoveSpeed = Mobility * 3, gated to zero when
HasMobility is false, even with positive numeric Mobility. Two pristine debug
legs therefore retain speed 6. MobilityMovementPolicy validates finite,
nonnegative, non-overflowing values; it is a mapping, not authoritative storage.
Simulation reads the typed speed, never JSON, physical slots or condition.
Client prediction reads the server's mirrored snapshot. GAS/condition/tags stay
outside rollback, are never restored/mutated by simulation, and the existing
physical state/input lists remain unchanged. Current ordinary projection may
change the result of replay, as in the accepted factory GAS/Netfox integration;
this does not introduce historical GAS or tick-scheduled stat transitions.
Jump eligibility/height/gravity are unchanged, including at zero horizontal
capability. Energy, Heat, PowerDepleted and ThermalCritical do not affect motion;
accepted idle/cooling simulation continues with no movement cost or heat input.

Only deterministic latency-sensitive physical inputs belong in Netfox rollback.
Discrete gameplay actions use semantic InputMap actions and mechanic-specific
server-validated reliable requests. Do not put arbitrary interaction buttons in
rollback, read physical keys in gameplay, or create speculative generic action RPCs.
Bindings/remapping remain Godot/Maaack configuration. No new discrete mechanic
or future install/detach action is implemented here.

Presentation copies completed simulation and interpolates a separate top-level
transform. Primitive torso/sensor geometry faces horizontal motion and derives
debug color from represented owner. Only the owning peer's camera activates and
follows the presentation. Camera, color and direction are outside rollback.

## Validation path

Build GameFactory.csproj and run both complete dotnet test projects, then diff check.
The accepted `run_mech_probe.ps1` Offline and Multiplayer regressions remain intact.
`run_playable_mech_probe.ps1` uses Import, Offline and Multiplayer modes with unique
run IDs. It extracts the configured production OnlineGameplayWorld from the normal
shell, replacing only the session transport with local ENet, above the accepted
Steam boundary. It does not launch the Host/Join menu or claim new Steam acceptance.

The playable probe checks normal configuration, spawn data, lifecycle identity,
full native GAS/resources, owner camera/authority/property lists, actual semantic
input-driven bidirectional Netfox movement and jumps, stationary convergence,
nineteen rejected client mutations per body at seven snapshots, and two rounds
with fresh objects/parts/resources and Netfox registration teardown. Headless
interpolation is explicitly enabled by the probe. Resource timers pause only in
the probe after spawn so exact canonical snapshots can be compared.
Task 06 extends this same path with ten controlled Mobility cases, twenty
additional matched client snapshots and ten client-owner displacement samples.
Each physical measurement drives one semantic action for 24 Netfox ticks
(0.8 seconds at the current 30 Hz). Moving tolerance is max(0.3, 10% expected
distance); stopping tolerance is 0.05 world units. Every matched snapshot checks
canonical native GAS, read-only movement projections, authority and presentation.
The scenario remains bounded to 90 seconds; import remains bounded to 60 seconds.

Runners preserve logs/contracts/source and assembly hashes under artifacts,
enforce bounded readiness/scenarios, use disposable APPDATA, restore/delete the
profile, verify zero owned processes and released ports. Runtime failures freeze
the attempt; investigate one hypothesis and obtain retry authorization before
changing/rerunning acceptance.

Movement resource costs, field parts, repair,
hazards, death/core transfer, extraction, persistence and final presentation,
camera, animation/UI/audio remain deferred.

## Task 05 acceptance — October 12, 2026

Build passed with zero warnings/errors; all 67 product and 73 foundation cases
passed, without failures/skips. No new pure logic was introduced, so no fake
engine-independent movement tests were added. Diff whitespace validation passed.

The final required runs reached PASS/complete:

| Run ID | Assertions / evidence directory |
| --- | --- |
| `mech_v012_routing_20261012_01` | Import, real SceneLoader Host/direct Join/invite/+connect_lobby, fallback, pending-invite rejection and one-shot intents; `artifacts/online_gameplay_routing/mech_v012_routing_20261012_01/` |
| `mech_v012_offline_20261012_01` | Existing body/damage/native GAS/resources and automatic resource cadence; `artifacts/mech_body/mech_v012_offline_20261012_01/` |
| `mech_v012_multiplayer_20261012_01` | Existing twelve-stage late-sync, damage, modifier/resource, 228 rejection and cleanup regression; `artifacts/mech_body/mech_v012_multiplayer_20261012_01/` |
| `playable_v012_offline_20261012_01` | Configured routing resolver, actual world/loadout, physical movement/jump and two-round lifecycle; `artifacts/playable_mech/playable_v012_offline_20261012_01/` |
| `playable_v012_multiplayer_20261012_02` | Configured routing resolver, bidirectional owner movement/jump, seven canonical/convergence snapshots, 266 client rejections and two rounds; `artifacts/playable_mech/playable_v012_multiplayer_20261012_02/` |

The host moved X from -2 to -6.7999973; the client moved Z from 0 to -4.799999,
with the other body stationary in each case. Both local jumps recorded one edge
and showed a 0.7667786 rise. The client observed host jump height; the server
observed client jump height. Stationary remote positions converged within 0.2 and
presentation within 0.25 world units. Both processes loaded the normal configured
shell's mech PlayerScene and checked owner camera/split authority. Both round
teardowns removed body/GAS/resource state and Netfox registrations; the second
round spawned new object/part IDs and initialized full resources, retaining transport.

The game-owned runners record source/build unchanged, zero owned processes,
APPDATA restored and profile removed. The upstream routing runner also records
zero processes/profile removal and restores settings and both profile variables.
Both multiplayer ports were released;
no VM was used. Native/input packets arriving after despawn were safely skipped
by Netfox's existing unknown-identity handling; there were no engine/script errors.
Evidence is local and intentionally ignored by Git. No subjective free-play or
new Steam/VM acceptance is claimed. Framework-owned paths remain unchanged.

Normal routing evidence has two focused boundaries: the official upstream probe
executes all four routes through the real loader to a session-free configured
fixture; the downstream playable probes assert that the same resolver and normal
menu select `res://game/shell/online_gameplay.tscn` with the actual mech PlayerScene.
No new Steam transport/session acceptance is claimed.

`playable_v012_multiplayer_20261012_01` completed gameplay assertions but was FAIL
at final cleanup error inspection: Netfox time synchronization attempted an RPC
after the probe detached its client peer. Processes/profile/port still cleaned.
After explicit user approval, only the probe disconnect order changed: close the
attached ENet peer, emit the MultiplayerAPI disconnect lifecycle so NetworkEvents
stops Netfox, await a frame, then detach/dispose. A clean rebuild and the approved
two-peer retry (`..._02`) passed. Gameplay source stayed unchanged; previously
passing unit, routing, existing mech and offline runs were not repeated for this
probe-only teardown fix. The earlier pre-v0.1.2 runs and routing-blocker record
remain preserved under their original IDs.

## Task 06 acceptance — October 12, 2026

Accepted: horizontal locomotion consumes final canonical GAS Mobility through
the getter-only playable projection and the tunable Mobility * 3 conversion.
Build passed with zero warnings/errors. All 80 product tests (13 new pure mapping
cases) and all 73 foundation tests passed with zero failures/skips. Diff whitespace
validation passed. No factory, addon, foundation test or upstream tool edits.

The unchanged existing mech offline and twelve-stage multiplayer regressions were
rerun because the movement consumer depends on accepted canonical GAS/modifier
behavior. They retain damage, resources, additive-effect coexistence, late sync,
228 client rejections and cleanup coverage. No prior regression was replaced.

| Run ID | Result / preserved evidence |
| --- | --- |
| `mobility_import_20261012_01` | PASS; `artifacts/playable_mech/mobility_import_20261012_01/` |
| `mech_mobility_offline_20261012_01` | PASS; `artifacts/mech_body/mech_mobility_offline_20261012_01/` |
| `mech_mobility_multiplayer_20261012_01` | PASS; `artifacts/mech_body/mech_mobility_multiplayer_20261012_01/` |
| `mobility_offline_20261012_01` | PASS; `artifacts/playable_mech/mobility_offline_20261012_01/` |
| `mobility_multiplayer_20261012_01` | PASS; `artifacts/playable_mech/mobility_multiplayer_20261012_01/` |

All runtime runs reached complete, with no failed stage or retry. Result records
verify frozen source/build, zero owned processes, restored APPDATA, deleted
disposable profiles and released multiplayer ports. No VM or Steam session test
was needed. Existing configured-scene assertions and two-round cleanup still pass.

| Setup | Structural Mobility | Final GAS Mobility | HasMobility | Speed | Expected displacement / 24 ticks |
| --- | ---: | ---: | --- | ---: | ---: |
| Pristine | 2 | 2 | true | 6 | 4.8 |
| Left leg half damaged | 1.5 | 1.5 | true | 4.5 | 3.6 |
| Left leg destroyed | 1 | 1 | true | 3 | 2.4 |
| Existing +0.5 native modifier | 1 | 1.5 | true | 4.5 | 3.6 |
| Right leg half damaged, modifier retained | 0.5 | 1 | true | 3 | 2.4 |
| Modifier removed | 0.5 | 0.5 | true | 1.5 | 1.2 |
| Modifier reapplied | 0.5 | 1 | true | 3 | 2.4 |
| Both legs destroyed, modifier retained | 0 | 0.5 | false | 0 | 0 |
| Modifier removed, zero baseline | 0 | 0 | false | 0 | 0 |
| Authoritative probe setup restores pristine conditions | 2 | 2 | true | 6 | 4.8 |

Offline and both peer-owned bodies matched all ten values. Server-observed client
displacement and client-predicted displacement converged; measured moving errors
were below 0.00004 units, with zero displacement in both stop cases. Owner input
did not move the other body. The client matched all 27 canonical/presentation
snapshots (7 baseline + 20 Mobility) and rejected 19 mutation paths for each of
two bodies at every snapshot: 1,026 rejections. Projection getters have no setters.

Healthy and zero-capability host jumps retained one recorded edge and about
0.7668 rise, using unchanged jump/gravity rules. Paused probe resource values stayed
unchanged during movement; the existing regression separately proves normal
automatic resource simulation. Restored setup and a fresh second round returned
to pristine Mobility/speed, new part/object identities and full resource setup.
Structural condition setup is probe-only, not a repair mechanic. No movement
Energy cost, movement Heat, resource-tag penalty, sprint/dash, jump scaling,
hazard, field-part, repair, core/death or presentation mechanic was introduced.
