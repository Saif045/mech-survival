# Netfox diagnostic probes and integration evidence

Canonical custom-player topology, authority, history and clock-lifetime contracts
live in [Netfox README](../networking/netfox/README.md). Use this document only
for these probe/instrumentation details; current acceptance is indexed in
[current state](current-state.md). Pinned upgrade provenance and fresh-import
observations live in [compatibility notes](netfox-compatibility-spike.md) and
[ownership migration](v0.2-ownership-migration.md#fresh-cache-validation-observation).

## Phase-1 probe and evidence

`factory/sandbox/netfox/netfox_time_probe.tscn` is selected by:

```text
--run=netfox
--test-scenario=netfox_time_sync
```

It uses the existing `SteamPlatform` and `SteamSession`. The probe observes
Netfox's actual signals and records them through `GameLog`; it does not create a
second logger or session abstraction. Relevant `netfox.time` events are:

```text
probe_ready
initial_sync_complete
client_sync_complete
tick_progress
client_sample_sent
client_sample_received
stopped
```

Metrics include role, lobby and Godot peer IDs, tick/time values, local and
remote tick/time, client RTT, configured tickrate, initial-sync timing, and
monotonic-tick evidence. The probe waits for 30 ticks after initial sync before
reporting a sample, so it does not treat singleton existence as a running tick
loop.

`factory/tools/ab_test/run.ps1 -Scenario netfox_time_sync` keeps the existing A--F
Steam/Godot checkpoints, then proves host/client time sync, host observation of
client sync, monotonic host/client ticks, a client RTT sample, and
`NetworkEvents`-owned stop events. A Steam/Godot failure before Netfox time
sync is attributed to its earlier layer rather than Netfox.

## Interactive movement playground

`factory/sandbox/netfox/netfox_gameplay_probe.tscn` is selected by `--run=netfox-gameplay`.
Launch one instance with `--steam-host`, then a second instance with the printed
`--steam-lobby=<id>`. Each process controls only its own bright outlined marker
with WASD. The host marker is blue and a client-owned marker is orange.

The sandbox follows Netfox's responsive-player-movement pattern: `Input` gathers
WASD once per `NetworkTime.before_tick_loop`; `Simulation` advances only from
that input in `_rollback_tick`; `RollbackSynchronizer` records
`Input:movement` and `Simulation:simulated_position`; and `TickInterpolator`
smooths that same state property for presentation. Player root and simulation
remain server-owned, while only `Input` uses the owning peer's authority. The
scene roots and authorities are established by the reusable
`NetfoxRollbackPlayerComponent`, a direct `NetworkObject` child. It runs in
`_EnterTree()` before the host's Netfox children perform their normal
enter/ready initialization; the sandbox does not manually invoke
`process_settings()`. The component owns only this split-authority/root glue;
the prefab retains its explicit Netfox property lists and its sandbox-specific
input, simulation, and presentation behavior.

## Reusable 3D rollback-player slice

The reference composition is documented in [starter player](../starter/player/README.md);
its reusable contract is [Netfox README](../networking/netfox/README.md).

`factory/sandbox/netfox/netfox_player_3d_probe.tscn` is selected with
`--run=netfox-player-3d`. It spawns the prefab through `NetworkWorld` and
`PlayerLifecycle`, supplies a floor/light/camera, and records structured local
and remote walking/jump observations for manual two-account acceptance. It
also emits the standard `steam.peer_status` initial, changed, and periodic
records so the shared A/B harness can establish native transport readiness
before its Godot and gameplay checkpoints.

The probe also exercises the [interaction contract](../gameplay/interaction/README.md)
and local owner-color presentation in the starter.

This is a manual playground, not the former deterministic divergence/convergence
acceptance scenario. Its small `netfox.movement` logs report player spawn and
configuration, input activation, and rate-limited local/remote movement.

The component extraction smoke `netfox_component_20260912_195800` verified the
split authority topology on both peers and visual bidirectional movement. The
existing harness recorded a false-negative failure at
`host_movement_observed_by_client`: it required one rate-limited
`remote_player_moved` event within its observation window, although the client
showed advancing host-player state history, a changing presentation, and later
emitted that remote movement event. That acceptance predicate is tracked as a
separate harness concern; it is not evidence that the component failed to
replicate movement.

When investigating a two-account run, each player additionally writes one
`netfox.history_age/sample` and one `netfox.transport_cadence/sample` event per
second. They record Netfox time/rollback ticks, known input/state ticks and
ages, prediction state, peer status, available Godot peer packets, and the
number and size of known-history advances in the preceding sample window.
The probe also writes an explicit `steam.peer_status` record when the native
peer status changes and once per second, so the transport handshake can be
attributed before Netfox player/topology diagnostics begin.
`netfox.history_age/threshold_crossed` is emitted once per player/history kind
at 32, 48, 56, and 64 ticks. These are diagnostics only: they do not alter
Netfox history size, authority, prediction, input broadcast, movement, Steam,
or Godot polling.

`netfox.reconciliation` records correction windows rather than every non-fresh
Netfox tick. A `correction_window_started` event is emitted only when a
non-fresh re-simulation produces a different result for the same simulation
tick. Once per second at most, `correction_window_summary` reports that
window's first/last replay ticks, replayed-tick count, number of same-tick
corrections, maximum correction magnitude, maximum presentation error, and
whether the visual state returned within the convergence bound.
`max_same_tick_correction_pixels` is the distance between a previously cached
and a re-simulated result for the same simulation tick; it is not a guessed
transport or clock offset. The current
scene draws the separate `Presentation` node, which copies the completed
simulation state after a tick loop and is then driven by `TickInterpolator`.
`presentation_sample` records the visual transform's distance from the
rollback simulation and its one-second maximum. After a non-zero correction,
the next correction-window summary reports whether the visual state returned
within an 8-pixel bound. This preserves simulation and authority ownership
while making visual convergence observable.

Use repeated sampling only after a baseline attempt succeeds:

```powershell
.\factory\tools\ab_test\run.ps1 -Scenario steam_basic
.\factory\tools\ab_test\run_suite.ps1 -Scenario netfox_time_sync -Attempts 3
```

Each process scenario follows `factory/docs/testing-protocol.md`; the suite preserves
separate per-attempt evidence and cleanup results.

For `netfox_gameplay`, the A/B harness establishes each manual checkpoint with
the shared event UTC rather than process-local elapsed time. It requires the
source player to show a post-input simulated position change and the receiving
peer to show a post-input change in that remote player's
presentation-versus-simulation distance. This prevents pre-connection input
from satisfying a later stage and avoids treating state-tick traffic alone as
movement evidence.

## Player vital respawn boundary

The stable reset/replay contract is documented in
[starter GAS](../starter/gameplay/gas/README.md#gas-and-netfox).

The headless `factory/sandbox/netfox/player_vital_motion_probe.tscn` loads the prefab's
actual `RollbackSynchronizer.state_properties` list into `_PropertyPool`, then
registers and pushes those properties through `NetworkHistoryServer`.
It inspects `_get_rollback_state_snapshot(21)` and restores ticks 20/21 with
`_restore_rollback_state` to prove reset replay and resumed motion using the
production simulation script and fixture player/input/GAS projections.
It also proves canonical
incapacitation overrides stale movement/jump/dash inputs while gravity settles
the body. Immutable A/B run `player_vital_lifecycle_20261006_081300` proves
normal and equipped respawn with unchanged network identity (attempts 002/006).
