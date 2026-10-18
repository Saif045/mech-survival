# GameFactory Netfox integration

Use Netfox for continuous latency-sensitive physical simulation. Netfox owns
time synchronization, input/state history, prediction, rollback/resimulation and
interpolation. GameFactory owns network spawning/identity and the small
`GameFactory.Networking.Netfox.NetfoxRollbackPlayerComponent` topology glue.
Games own input fields, simulation rules and visual composition. This is not a
wrapper replacing raw Netfox APIs or a second reconciliation system.

The vendored dependency is pinned to source commit
`38f59778b02bfd1a3dedc7dcc985945d7058858d` (reports 1.49.3), under
`addons/netfox/` and `addons/netfox.internals/`. Keep project autoload/plugin
settings and history 64 / input broadcast false / full-state interval 24.
Raw APIs are documented by [vendored Netfox](../../../addons/netfox/README.md)
and its [RollbackSynchronizer source](../../../addons/netfox/rollback/rollback-synchronizer.gd).
Provenance/upgrade evidence lives in [compatibility notes](../../docs/netfox-compatibility-spike.md),
not in the normal custom-player reading path.

## Minimal custom player

```text
GamePlayer (CharacterBody3D, game script)
  NetworkObject                 # normal factory network_object.tscn; SpawnGroup=Players
    NetfoxRollbackPlayerComponent
  Input                         # game input script, owned by represented peer
  Simulation                    # game _rollback_tick script, server authority
  Presentation (Node3D)         # game visual script
  CollisionShape3D
  RollbackSynchronizer          # raw Netfox script
  TickInterpolator              # raw Netfox script
```

NetworkObject and its glue must enter before the Netfox children. Spawn through
NetworkWorld so binding exists before tree entry. Glue paths `InputPath`,
`SimulationPath`, `RollbackSynchronizerPath`, `TickInterpolatorPath` default to
the names above and resolve relative to the **host**, despite the component
being under NetworkObject. The glue sets both Netfox roots to Host, host and
Simulation authority to server peer 1, and Input authority to OwnerPeerId,
non-recursively. It does not assign presentation to a client or configure
simulation/property lists for you. OwnerPeerId remains represented metadata.

For a simple position/velocity mover, author RollbackSynchronizer properties
relative to the player root, for example:

```text
state_properties = [":position", ":velocity", "Simulation:grounded"]
input_properties = ["Input:movement", "Input:jump_pressed"]
enable_prediction = true
enable_input_broadcast = false
TickInterpolator.properties = ["Presentation:position"]
```

These are game-authored properties, not fields provided by the glue. Implement
input gathering and `_rollback_tick(delta, tick, is_fresh)` in game scripts;
the [working starter](../../starter/player/network_player_3d.tscn) and
[simulation](../../starter/player/network_player_3d_simulation.gd) show the real
callbacks. Queue edge input such as jump between ticks. Use
`NetworkTime.physics_factor` only around `move_and_slide()`. Presentation copies
completed simulation state and interpolates it; cameras and cosmetic owner
colors remain local. Do not interpolate the authoritative physical root.

## History, prediction and external gameplay

Every value that determines a rollback result must be synchronized or derived
deterministically from synchronized data. Include mutable physical consumption
state (for example last applied reset revision) in state history, so restoring
an older snapshot can replay the boundary. `is_fresh=false` means resimulation;
a correction is a changed result for the same tick, not every replay.

Prediction permits responsive owner motion before server results arrive;
server simulation still decides truth. TickInterpolator smooths presentation
after the tick loop. Neither changes canonical GAS or validates discrete RPCs.

GAS attributes/effects/tags, inventory, carry, switches, diagnostics and session
state stay outside rollback. Canonical server GAS → explicit ordinary projection
→ physical simulation is the boundary. Do not snapshot the ASC or mutate effects
on replay. A game consuming changing external projections must define its
tick/revision behavior explicitly; the glue does not automatically make arbitrary
GAS values replayable. Starter dash uses authorization revision and replayable
physical dash state; respawn uses ordinary revision/tick plus rollback-recorded
reset consumption. See [starter GAS](../../starter/gameplay/gas/README.md#gas-and-netfox)
when adapting those semantics.

## Clock and retained-session lifetime

SteamSession installs/removes the peer below Godot MultiplayerApi. Netfox
`NetworkEvents` alone owns `NetworkTime.start()` / `stop()` in response to that
lifetime. Do not call them from player ready, world reset or round content.
Do not call `process_settings()` as normal initialization.

Round reset despawns players/content while retaining peer, world allocator and
Netfox autoloads. Keep NetworkObject paths and Netfox identity allocation
monotonic; never call identity clear during a live session. Netfox's persistent
command/synchronization receivers handle packets for removed subjects; do not
reintroduce per-player input RPC transport. Terminal peer teardown ends time;
the shell owns the ordering, not the custom player.

## Validation and mistakes

Focused guards: [late-input runner](../../sandbox/shell/run_netfox_late_input_probe.ps1),
[role/round RPC runner](../../sandbox/shell/run_round_rpc_probe.ps1) and
[physical vital history probe](../../sandbox/netfox/player_vital_motion_probe.tscn).
Custom-player lifecycle: [runner](../../sandbox/shell/run_custom_player_lifecycle_probe.ps1).
Use [testing protocol](../../docs/testing-protocol.md) for process execution;
Steam/VM evidence requires the operator protocol too. Current acceptance →
[current state](../../docs/current-state.md). Diagnostic sampling and historical
probe records → [integration evidence notes](../../docs/netfox-integration.md).

Common mistakes: recursive owner authority, glue entering too late, property
lists that omit physical state, direct process-frame teleports, ASC in history,
effects executed again on replay, clearing live IDs, and diagnosing rollback
when transport never connected. Discrete gameplay, transport replacement and
automatic compatibility with arbitrary game physics are non-goals.
