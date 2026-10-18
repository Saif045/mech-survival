# Current State

This is the compact source of current maturity and evidence. It is not a
replacement for architecture or protocol documentation. The accepted v0.1
runtime baseline is `2ea72888989eed883b09065c741c6410aa17077d` (`2ea7288`),
accepted October 7, 2026.

## v0.2 physical ownership acceptance — October 12, 2026

Task D follows accepted Tasks A–C at `7a1cca7342`. First-party mechanisms,
starter, sandbox, tests, tools, docs and evidence now live under `factory/`.
Reference player/GAS/inventory/permissions/revive/switch/cube and default round/
spawn composition are under `factory/starter/`; reusable source has no starter
or game type/resource dependency. See [ownership](ownership.md),
[migration](v0.2-ownership-migration.md) and
[ADR 0008](decisions/0008-runtime-starter-consumer-ownership.md).

The local acceptance record is `factory/artifacts/ownership/task_d_20261012_01/result.json`:
build zero warnings/errors; all 73 unit tests; moved UID companions and authored
scene/resource headers unchanged; local Markdown destinations and PowerShell
parser checks clean. Runtime evidence includes:

- `factory/artifacts/interaction_boundary/task_d_final_20261012_02/`: import,
  default starter GAS/player composition and custom actor interaction.
- `factory/artifacts/carry_boundary/task_d_final_20261012_01/`: custom/starter
  carry pairs, holder contention, permission and inventory/equipment compatibility.
- `factory/artifacts/round_composition/task_d_final_20261012_01/`: default content,
  three-round reuse/custom-player lifecycle, custom and empty-content pairs.
- `factory/artifacts/online_gameplay_routing/task_d_final_20261012_01/`: real Maaack
  loader, default/fallback and all configured online entry routes.
- `factory/artifacts/netfox_compatibility_spike/task_d_netfox_20261012_01/` and
  `task_d_late_input_20261012_01/`: lifecycle, motion/reset/history, repeated reuse
  and safe held stale-packet handling. Disposable wrapper evidence is under
  `factory/artifacts/ownership/` with those labels.

All successful runs reached PASS, zero owned processes and removed disposable
profiles. Fixture-only A/B startup checks passed in Windows PowerShell, the
harness's configured runtime; no Steam/VM A/B was rerun. Source hashes stayed
unchanged throughout final runtime acceptance. No gameplay semantics changed;
GameShell's default fallback moved from a public starter-path constant to
explicit exported composition data.

A deliberately deleted Godot import/script/UID cache exposed vendor Netfox
first-scan autoload type-inference errors. That attempt is preserved as FAIL at
`factory/artifacts/interaction_boundary/task_d_final_20261012_01/`. After cleanup,
a user-authorized controlled retry passed with identical source/build and the
metadata populated by the completed scan. Vendor code was not modified; this
cold-cache first-scan limitation is recorded rather than hidden as a path fix.

## Proven foundation

- Steam listen-server transport, Godot `MultiplayerAPI`, `NetworkWorld`, and
  `PlayerLifecycle` have accepted two-account Hyper-V evidence.
- Steam lobby/peer lifecycle isolation is accepted for tested fresh join,
  same-lobby rejoin, new-lobby reuse, and role reversal; retained closed peers
  sent no stale handshakes.
- The reusable Netfox source pin `38f59778b02bfd1a3dedc7dcc985945d7058858d`
  (reports 1.49.3) `CharacterBody3D` player composition is
  acceptance-proven: split server-state/client-input authority, rollback,
  interpolation, bidirectional walking/jumping, and queued one-shot jump.
- Gameplay → Lobby → Gameplay session reuse is acceptance-proven: fresh round
  state, monotonic object/Netfox identities while the host peer remains live,
  graceful leave/rejoin, equipped-client disconnect cleanup, terminal host
  Leave, stale-readiness rejection, and same-process role reversal. Real late
  input is safely skipped by the persistent Netfox receiver; no cached-node or
  invalid-packet RPC errors appeared. See [acceptance evidence](session-reuse-acceptance.md).
- The host-PC -> SSH/SCP -> GPU-P Hyper-V guest release and interactive-task
  path has verified build parity and cleanup behavior.

- Server-authoritative interaction is acceptance-proven: a local E press
  requests a target by `NetworkObjectId`; the server validates and toggles a
  replicated switch visible to the other peer. Deterministic per-owner player
  colors remain presentation-only.
- Server-authoritative pickup/carry/drop is acceptance-proven. It composes the interaction request path
  with one permanent server-owned carryable item. Held state is a replicated
  holder `NetworkObjectId`; each peer resolves its presentation `CarryAnchor`.
  The server computes world drop transforms and no carry state enters Netfox
  rollback. Both host and client have picked up, moved/jumped with, and
  dropped the item, with remote follow observed on the other peer.
- The first server-authoritative inventory slot is acceptance-proven. A single
  permanent, server-owned cube keeps its `NetworkObjectId` while transitioning
  explicitly between World, Carried, and Stored. The server validates one
  player slot, replicates only its stored-item identity for presentation, and
  never transfers item authority or places inventory state in GAS or Netfox
  rollback. Both owners completed carry → store → retrieve, with the remote
  peer observing the slot transitions.
- Equipment → GAS is acceptance-proven. The same permanent cube transitions
  between World, Carried, Stored, and Equipped without changing identity. The
  server alone grants and removes the `equipment_cube_move_speed` capability;
  ordinary GAS snapshots project its `6 → 12 → 6` speed change while Netfox
  continues to own only movement simulation. An Equip request with no stored
  item is rejected safely rather than constructing an invalid object identity.
- GodotGAS v1.0.6 is vendored under `addons/GodotGAS` and its editor plugin is
  enabled. GameFactory C# can create a real GodotGAS Ability System Component,
  call it, and read state back through the scoped GAS adapter.
- Networked GodotGAS Health is acceptance-proven: the represented owner makes a
  reliable, no-payload request; the server validates `OwnerPeerId`, activates
  the real `SelfDamage` ability, captures `GasSnapshot(Health)`, and replicates
  the ordinary authoritative projection. Non-server ASCs apply that snapshot
  as a local mirror. The host does not replay its own effect and Netfox remains
  outside the slice.
- The first GodotGAS × Netfox boundary is acceptance-proven through the
  temporary three-second SpeedBoost: canonical server GodotGAS owns a
  non-stackable `State.SpeedBoosted` effect and publishes the ordinary
  `GasMoveSpeed` projection. Netfox reads that current projection to calculate
  velocity, but never owns or restores it as rollback state; only movement
  position, velocity, and grounded state remain in rollback history. Both
  owners observed `6 → 18 → 6`, repeated activation was rejected while active,
  and the remote presentation retained the boost until authoritative expiry.
- Sprint + Stamina is acceptance-proven: Netfox records only sprint intent;
  server GodotGAS owns periodic Stamina drain/regeneration and the Sprinting /
  Exhausted tags; normal replication projects Stamina and sprint permission.
  Netfox reads that projection for `6 → 9 → 6` movement while Stamina and
  exhaustion remain outside rollback history.
- Predicted Dash is acceptance-proven: `dash_pressed` is a queued one-shot
  Netfox input, while the server alone activates the canonical GodotGAS Dash
  ability (25 Stamina, `Cooldown.Dash`, blocked by `State.Exhausted`). The
  server publishes only a small authorization revision and cooldown projection;
  Netfox owns the replayable dash duration/direction motion state. Local owner
  prediction begins from the input edge and the authorization revision lets
  normal rollback/reconciliation retain accepted motion or correct a rejected
  prediction. GAS effect/runtime objects remain outside rollback history.

- Player Vital Lifecycle is acceptance-proven: canonical server GAS Downed/Dead
  tags, HP50 teammate revive, server-owned 10-second bleedout and 2-second
  respawn to HP100/Stamina100 with unchanged player identity. Incapacitated
  actions are gated canonically; gravity continues. Carried items auto-drop,
  stored/equipped items and GAS capabilities survive death, and player despawn
  releases hidden inventory/equipment to World and removes equipment effects.

## Latest runtime evidence

October 12 Task C game-owned round-content/spawn-data composition passed on
baseline `c16442f` plus Task C changes. Local default smoke and three-round
lifecycle probes preserve starter content/positions and cleanup. Custom and
empty-content ENet pairs each passed two rounds with server-only pre-tree data,
host/client observation, player-first teardown, local content reset, fresh
object identities and retained peer; terminal Stop subscription cleanup also
passed. Run `task_c_20261012_01` records zero processes, removed profiles and no
runtime errors/leaks. Build passed with zero warnings/errors and all 73 tests
passed. Canonical APIs/migration are in
[shell README](../shell/README.md); [exact evidence](#task-c-detailed-acceptance-record) is below. No new Steam/VM evidence is claimed.

October 12 Task B player/item-independent carry passed local default GAS/vital/
inventory compatibility and custom/starter two-process ENet acceptance on
baseline `d7498d3` plus Task B changes. Explicit carry permission/anchor and
carry-only target capability replace hidden default-player dependencies.
Both owners picked up/dropped and observed held follow; starter movement/jump,
capacity, Downed release, inventory/equipment, sender/permission/holder rejection,
stale item and despawn cleanup passed. Run `task_b_20261012_02` records PASS,
zero test processes, removed profiles and no runtime errors/leaks. Build passed
with zero warnings/errors and all 73 tests passed. Canonical contract/migration
is in [carry README](../gameplay/carry/README.md); [exact evidence](#task-b-detailed-acceptance-record) is below.
This is local carry boundary evidence, not new Steam/VM acceptance.

October 12 player-independent interaction boundary acceptance passed local
default-player and two-process ENet custom-player checks on baseline `adf1a854`
plus Task A changes. `PlayerInteractor` now requires explicit actor permission;
the starter player retains canonical GAS incapacitation gates, while a plain
`Node3D` fixture needs neither `NetworkPlayer3D` nor `NetworkGasComponent`.
Host/client owner requests, switch replication, wrong-sender, invalid/missing
target, range, policy and self-target rejection passed, as did default input,
pickup and bidirectional revive. Build passed with zero warnings/errors and
all 73 tests passed. Run `task_a_20261012_06` records zero test processes,
removed disposable profiles and no runtime errors/leaks. Earlier failed
fixture/harness attempts remain preserved; see [exact evidence](#task-a-detailed-acceptance-record).
Canonical usage is in [interaction README](../gameplay/interaction/README.md).
This is local boundary evidence, not new Steam/VM acceptance.

October 7 session-reuse/Netfox acceptance is the newest Steam/Netfox milestone,
recorded at `2ea7288`. Frozen run `netfox_round_reuse_20261007_050603`
proved repeated fresh rounds, leave/rejoin, equipped-client disconnect cleanup,
host continuation and terminal Leave. Focused run
`readiness_roles_20261007_054650_run` proved stale/Gameplay readiness rejection
and same-process role reversal with balanced NetworkEvents starts/stops.
Real late unreliable input safely skipped removed identities through the
persistent receiver; no cached-node or invalid-packet RPC errors appeared.
Gameplay discovery hid the lobby as intended; the direct connected-peer
`late_join_rejected` fallback was not deliberately forced. Both checkpoints
preserved evidence and verified host0/VM0 cleanup. Recorded cheap validation
passed build (zero warnings/errors), 73 tests, lifecycle/history-motion/reuse
probes and the held stale-input regression. These are accepted prior results,
not checks rerun for documentation closure. See
[session reuse acceptance](session-reuse-acceptance.md) for build hashes,
the diagnostic-export deviation, log qualifications, and exact evidence.

## Earlier accepted evidence

Player Vital Lifecycle run `player_vital_lifecycle_20261006_081300` used one
fresh immutable export `gf_629f3e3a_c79372e6ca6e`, manifest
`3aaed13da6d78b69ea1b70709f16d87f2ca9dc7c7dc87457ffd32c60e4a946b1`.
Operator reports and structured evidence together pass the complete slice:
attempt 002 proves bidirectional HP50 revival, action restrictions, ~10s/~2s
bleedout/respawn with stable identities, automatic carry drops, and existing
gameplay; attempt 004 proves equipped-client disconnect, player removal,
equipment effect removal (12 -> 6), and recoverable cube 3; attempt 006 proves
client-owned equipped death/respawn to HP100 and speed12 with unchanged player
and item identity. The ordinary respawn revision/tick is consumed inside
rollback simulation; only reset consumption joins physical history. See
[Netfox integration](netfox-integration.md#player-vital-respawn-boundary).
Accepted logs have zero severe matches. Verify/Stop passed and independent
checks confirmed host=0 / VM=0. Evidence is under
`factory/artifacts/ab_tests/player_vital_lifecycle_20261006_081300/attempt_002`,
`attempt_004`, and `attempt_006` (`evidence.json` and host/client logs).
Deviation: missed manual steps required immutable retries, with no re-export,
source changes, or Steam restart. Attempt 001 stopped at sandbox Task Scheduler
access; attempts 003/005 closed the client before equipped respawn and are
preserved as incomplete checks. Cheap validation passed: C# build, 73 unit
tests, real GAS/composed-player probe (stale request rejection and both hidden
inventory cleanup cases), Netfox history/motion probe, and `git diff --check`.

App 480 development supports direct/harness launch; Steam overlay is not
guaranteed unless Steam owns launch. Invite Friends falls back to Join Game
when the overlay is unavailable. Real-App-ID launch normalization remains
future work. See [Steam integration](steam-integration.md).

Windows startup investigation at `62a8cfc` did not reproduce reported
`0xC0000142 / STATUS_DLL_INIT_FAILED`: 109/109 controlled startup probes passed
before reboot and 109/109 after reboot, including editor-closed and scheduled
task/profile/renderer comparisons. The defect remains unresolved and may recur;
these are baseline results, not post-fix acceptance. Diagnostic-folder discovery
caused separate duplicate Steam editor registrations and was excluded with
`.gdignore`. See [Windows startup investigation](windows-startup-investigation.md)
for evidence, limitations, and the capture procedure if the failure returns.

Netfox lifecycle run `netfox_lifecycle_fix_20261003_02`, attempt 001, passed
normal-shell gameplay (56.8 seconds common PC-host gameplay, then 75.7 seconds
with VM hosting in the same processes), movement/jump/switch acceptance, and
verified host/VM cleanup on build `gf_f7c129f4_d838f603ed97`. Exact traces show
one start, after-sync, and stop per peer/session, correct server-state/owner-input
authority, five unique autoloads, and clean rollback callback teardown.
Historical duplicate-start/self-RPC errors did not reproduce; their origin is
unproven. A confirmed host-close double stop was fixed in Netfox NetworkEvents
with immediate role-paired stop routing. No transport or RPC-target changes
were needed. See [Netfox lifecycle investigation](netfox-lifecycle-investigation.md)
for deviations, local vendor delta, upstream checks, and remaining warnings.

Steam lifecycle run `native_lifecycle_acceptance_d28f7ad_20261003_02` tested
unchanged `d28f7ad` runtime inputs. Attempt 001 passed all four lobby/peer flows.
Attempt 003, after an authorized Steam restart on both machines, passed fresh
join to ConnectedToServer and ready 2/2, Lobby → Start, mutual visibility,
movement/jumping, bidirectional switch interaction, and clean Leave. Host/VM
cleanup was verified. The retained-object native re-host probe also passed.
Details: [Steam peer lifecycle investigation](steam-peer-lifecycle-investigation.md).

Equipment → GAS run `equipment_guard_retest_20260926_034700` passed fresh
export, VM parity, two-player topology, visual host/VM verification, and
cleanup on immutable build `gf_e37a5552_dcc0c761c013`. Both owners completed
World → Carried → Stored → Equipped → Stored → Carried; authoritative logs
show `6 → 12 → 6` GAS speed projections, replicated remote movement, and a
safe empty-slot `equip_rejected` with no engine errors. Evidence:
`factory/artifacts/ab_tests/equipment_guard_retest_20260926_034700/attempt_001/evidence.json`
and its captured host/client logs.

Free-play run `carry_freeplay_retry_20260914_193933` passed infrastructure
startup, two-player topology, and verified cleanup on immutable build
`gf_240d7720_7984bcc0f537`. The operator visually confirmed bidirectional
movement/jumping, switch changes from either participant, and host/client
carry and drop. Structured logs agree: authoritative switch transitions,
replicated remote switch visuals, host- and client-owned pickup/drop cycles,
replicated holder state, remote-follow observations, and no severe events.

Evidence: `factory/artifacts/ab_tests/carry_freeplay_retry_20260914_193933/result.json`,
its captured client JSONL, and the corresponding host run JSONL.

Networked GAS Health run `gas_network_activation_retry_20260914_211000` passed
infrastructure, topology, manifest parity, and cleanup on immutable build
`gf_0c265dd3_2ae74507f072` (`0c265dd`). The operator confirmed both players'
HP changes on both views through 0. Structured logs prove server-only real
GodotGAS activation after owner validation, authoritative
`GasSnapshot(Health)` publication, non-server snapshot mirror application, no
host double-apply, no rejected requests, and no severe events. Evidence:
`factory/artifacts/ab_tests/gas_network_activation_retry_20260914_211000/result.json`

GAS × Netfox SpeedBoost run `gas_speedboost_boundary_20260915_023000` passed
infrastructure, immutable-build parity, topology, and cleanup on
`5bcfbaf`. The operator confirmed the exact host/client and remote visual
contract. Structured evidence shows server-only owner activation, repeated
active-window rejections, authoritative `6 → 18 → 6` effect lifecycles, and
client speed projections that remain at `18` instead of being rollback-reset
in the same frame. No severe events occurred. Evidence:
`factory/artifacts/ab_tests/gas_speedboost_boundary_20260915_023000/result.json` and
its captured host/client JSONL.

Sprint + Stamina run `gas_stamina_sprint_20260915_024000` passed on immutable
build `4f213f5`. Both owners drained `100 → 0` while sprinting, were exhausted
at zero and returned to speed 6, regenerated after release, cleared exhaustion
at 27, and sprinted again. Replicated client projections matched server state;
no severe events occurred. Evidence:
`factory/artifacts/ab_tests/gas_stamina_sprint_20260915_024000/result.json` and its
captured host/client JSONL.

Predicted Dash run `gas_dash_20260915_235900` passed infrastructure, fresh
immutable-build parity, topology, and verified cleanup. The operator visually
passed Dash on both participants. Structured evidence shows the client-owner
input and immediate predicted-start events, server-only canonical acceptance,
each accepted activation's `25`-Stamina cost and `0.75s` cooldown projection,
and replicated authorization revisions consumed by the normal Netfox motion
path. Host-owned activations followed the same server-owned lifecycle. No
severe events occurred. Evidence:
`factory/artifacts/ab_tests/gas_dash_20260915_235900/result.json` and its captured
host/client logs.

Inventory run `inventory_slot_replication_20260915_004100` passed
infrastructure, fresh immutable-build parity, topology, and verified cleanup.
The operator visually passed the same host/client flow as the preceding run.
Structured evidence proves that the server accepted store/retrieve for both
owners, the cube retained `NetworkObjectId` `3`, and the client received the
replicated slot state `0 → 3 → 0` for each owner. No severe events occurred.
Evidence: `factory/artifacts/ab_tests/inventory_slot_replication_20260915_004100/result.json`
and its captured host/client JSONL.

## Transport history

BadCert remains unresolved: attempt 002 of the Steam lifecycle run failed
before Godot admission with certificate errors and unrecovered connection /
rendezvous timeouts. The later Steam-restart comparison passed, but does not
prove the cause, GameFactory ownership, or permanent resolution. The original
second full four-join cycle remains incomplete. Preserve this failure separately
from the accepted lifecycle isolation. The subsequent Netfox investigation
accepted the normal gameplay/process-reuse lifecycle described above.

The free-play launch immediately preceding the accepted run reached lobby
membership and peer assignment but remained native `Connecting` for 120
seconds. It was cleanly terminated; the next unchanged retry reached two-player
topology and passed. Treat this as an intermittent Steam/native transport
symptom, not a carry, interaction, Netfox, or movement defect. Preserve the
evidence and investigate it only if it recurs.

## Recent commits

- `2ea7288` — accepted reusable gameplay sessions and persistent Netfox input routing.
- `e462e70` — accepted player-vital lifecycle.
- `629f3e3` — fresh graphical export resolution and host-readiness gate.
- `94c8ec1` — simplified Steam development launch handling.
- `898b357` — explicit Steam launch/overlay policy.
- `a9899d7` — unreproduced Windows startup failure record and diagnostic import exclusion.
- `62a8cfc` — role-paired Netfox stop lifecycle and runtime acceptance.
- `f7c129f` — accepted Steam lifecycle and BadCert qualification.
- `d28f7ad` — native GodotSteam callback lifecycle isolation, rebuilt DLLs,
  reproducible patches, and opt-in diagnostics.
- `45fcd36` — export-helper completion fix and lifecycle/gameplay acceptance record.
- `81458f2` — server-authoritative carryable item.
- `240d772` — carry acceptance checkpoint sequencing (superseded for normal
  free play by the infrastructure-only harness).
- `574b16d` — infrastructure-only A/B harness and operator workflow.
- `0c265dd` — accepted server-authoritative networked GodotGAS Health slice.
- `5bcfbaf` — accepted non-stackable GodotGAS SpeedBoost / Netfox boundary.
- `4f213f5` — accepted GAS Sprint + Stamina / Netfox boundary.
- `cb9918f` — accepted predicted GAS Dash / Netfox boundary.
- `6f65e67` — accepted server-authoritative one-slot inventory state machine.

## Consumer scene-routing provenance

Consumer routing provenance: the downstream `Saif045/mech-survival`
reproduction exposed that Host honored the configured gameplay scene while
Join and Invite loaded the factory scene directly. All routes now share
resolution; the Task D routing probe/evidence above remains the behavioral
baseline. This documentation pass did not rerun that probe.

## Task A detailed acceptance record

Task A acceptance passed October 12, 2026, on baseline `adf1a854` plus this
task's source changes: `factory/artifacts/interaction_boundary/task_a_20261012_06/`.
`result.json` records PASS, zero live test processes and removed disposable
profiles; host/client stdout records each assertion and ordinary switch
replication. The tested Debug assembly SHA256 is
`D82DFE6E43B43595D963D23C0343592FA1AB57361395C3D541B0111491C527B7`.
The default probe passed input/nearest switch, interaction pickup, canonical
Downed/Dead rejection, bidirectional revive and existing vital/equipment cleanup.
Build passed with zero warnings/errors; all 73 tests passed (final TRX under
`factory/artifacts/interaction_boundary/task_a_unit/`). No new engine-independent
policy algorithm was introduced; Godot policy configuration is exercised by
the runtime fixture.

Earlier evidence is preserved separately: attempt 01 was blocked at sandbox
CIM preflight; attempts 02/03 passed gameplay assertions but failed fixture
exit checks; diagnostic 04 stopped on verbose editor socket output and 05
identified orphan invalid-policy test nodes. The fixture now explicitly frees
those nodes, pairs signal subscriptions, uses a frame deadline and tolerates
process exit racing with Stop-Process while independently verifying cleanup.
These corrections did not change the production interaction behavior.


## Task B detailed acceptance record

Task B passed on October 12, 2026, baseline `d7498d3` plus the task changes.
Final local suite: `factory/artifacts/carry_boundary/task_b_20261012_02/`; its result
records PASS, zero live test processes, removed profiles and no VM use. The
Debug assembly SHA256 in build.json is
`8E6615C02009ABDD0480B43ECAD5D7880870DA6E4CC3F22F7AD7B40F8DB767F1`.
Default GAS/vital/inventory tests and both custom/starter ENet pairs passed,
including replicated moving-anchor follow, starter movement/jump while held,
permission/sender/holder rejection, removed-item recovery and world cleanup.
Build passed with zero warnings/errors; all 73 existing tests passed (final
TRX in `factory/artifacts/carry_boundary/task_b_unit/`); diff check passed. New
configuration/capability logic depends on Godot nodes and is covered by runtime
assertions rather than mirror unit tests. No new engine-independent algorithm
was introduced.

Candidate suite `task_b_20261012_01` also passed and is preserved. Final review
corrected the custom fixture to put its shape under StaticBody3D, added the
collision-topology assertion, then reran the complete suite. This is focused
local mechanism evidence, not new Steam/VM or visual A/B acceptance.

## Task C detailed acceptance record

October 12, 2026: baseline `c16442f` plus Task C passed
`factory/artifacts/round_composition/task_c_20261012_01/`. The result records PASS,
zero owned processes, removed disposable profiles and no VM use. Debug assembly
SHA256: `033CC068E06FA1504DEA34DB42BF4BF85DCA4AB008AA0195DD9BD39B641F5393`.
Build passed with zero warnings/errors; all 73 tests passed (TRX under
`factory/artifacts/round_composition/task_c_unit/`); diff checks passed.

Default smoke passed interaction/pickup, GAS/vitals, all inventory/equipment
transitions and cleanup. Default round reuse passed three fresh populated rounds
with unchanged host/switch/cube positions; the existing custom-player lifecycle
probe passed three rounds with starter host/other-player initialization and
disconnect/player-first cleanup. Custom host/client pairs observed beacon/crate
content and both players' position/player-number/loadout data before tree entry;
two rounds cleared completely with object maxima 4 then 8. Empty-content pairs
spawned players only, with maxima 2 then 4, despite a content-capable node still
being present but unselected. Server-only start/data creation, local content reset
on both peers, invalid configuration, retained peer and terminal Stop cleanup
passed without runtime errors/leaks. The fixture inspects registry identities
through reflection, keeping test-only access out of the public lifecycle API.

No new engine-independent algorithm was introduced; NodePath/capability/data
composition is checked in Godot rather than duplicated in pure unit tests.
Existing Steam/VM acceptance remains prior evidence and was not rerun.
