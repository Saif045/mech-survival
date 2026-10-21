# Random expedition v1

The normal ten-minute run explores a branching open industrial landscape, salvages three machines,
and extracts every currently active mech. SurveyBeacon and the fixed corridor
are retired. Shell/player/disconnect/teardown ownership remains in GameFactory.

## Seed and layout

The server spawns one server-owned ExpeditionRun with an integer seed in spawn
initialization. Every peer synchronously generates its static collision geometry
from that seed before ExpeditionRun finishes entering the tree. Clients do not
choose seeds or advance run rules. Normal rounds choose a fresh random seed;
focused probes explicitly select fixed seeds. The seed and layout/environment signatures appear in debug output only.

ExpeditionLayout is a small pure generator with its own specified 32-bit LCG,
ordered frontier enumeration and SHA-256-derived 16-hex-digit layout signature.
Default size is 18 rooms (supported range 16–20), with 42-unit cardinal sectors.
An initial four-room square guarantees a loop; random frontier growth fills the
facility, and three reserved cells extend a service spur with a guaranteed dead
end. All adjacent cells connect symmetrically, providing further alternate paths.
No cells overlap. BFS supplies room paths and distances. Extraction is chosen
among the farthest and next-farthest rooms, preferring a leaf, then distance.
The signature includes cells, templates, edges, extraction and encounter rooms.

## Editable rooms

Six authored primitive scenes live in `game/world/rooms/`: yard, storage,
maintenance, machinery, loading and damaged. Each has its own floor material,
landmark and obstacle dimensions. Edit these scenes for level-design changes.
Keep a clear cross from the center to each cardinal doorway; preserve the
positive-X/positive-Z salvage bay at (+4,+4). Geometry is authored, not randomized
prop scatter. ExpeditionRun adds low industrial debris banks, container barriers, pipe walls
and tanks on disconnected edges, with 16-unit open passages on connected edges.
Sparse enlarged obstacles leave sightlines between sectors and clear center routes. Generated geometry belongs to the run object
and disappears with it on reset.

## Encounters and objective

A deterministic shuffle chooses five distinct salvage rooms and three distinct
parasite rooms, excluding entry and extraction and separating encounter kinds.
Machines use the existing hacking challenge, self-destruct failure and exact
industrial_cooling 90% / high_density_power 85% stored reward instances.
Successful Disabled machine identities count once. Failed machines do not count.
Three successes unlock extraction once; terminal Success/Failure cannot change.

Parasites exist from entry. The server chooses the nearest eligible mech by BFS
room distance, local distance, then object identity. Detection spans three room
connections. Outside detection, parasites roam between adjacent room centers at
speed 2. Chasing speed is 8, above pristine mech speed 6. Concrete BFS waypoints
follow room centers through cardinal doorway centers; within the target room,
the parasite closes directly on the player. Targets are reconsidered when bodies
move, disappear or become infected. A mech with an existing source claim is
ineligible. Replicated server positions drive remote roaming/chase presentation;
attachment retains the same object identity and existing anchor behavior.
Self/co-op purge, canonical Energy/Heat load and pulse validation are unchanged.
No NavMesh or general AI system is introduced.

## Extraction and HUD

Normal HUD shows three filled/unfilled salvage pips, the run timer, Energy and
Heat bars without numbers, one active hazard chip, brief damage/storm alerts and
a compact color-coded mech schematic. Safe/dormant zones carry no large warning.
A small STABILIZING state identifies valid safe recovery. Detailed resource and
part numbers live under Tab; seed, rates and signatures stay in debug output.
After quota, a tall green beam, floor ring and large world label activate in the
selected distant room. The HUD gives horizontal distance and compass direction.
Success needs every live active body inside the 4.5-unit extraction radius; zero
bodies cannot win. Existing disconnect removal supplies current membership.
The server advances the deadline before checking quota/extraction, and returns
the retained session to lobby once after a two-second terminal display.

## Validation and exclusions

Build `GameFactory.sln`, run `game/tests/MechSurvival.Tests`, then
`game/probes/run_playable_mech_probe.ps1 -RunLoop -Mode Offline` and
`-RunLoop -Mode Multiplayer` with unique run IDs; run `git diff --check`.
Evidence lives under `game/artifacts/run_loop/`. The fixed-seed probe checks
composition, real collision traversal, graph chase/catch, self or teammate purge,
three real hacks, quota replication, all-active extraction and reset into a
second seed with fresh world/reward identities. Headless probes do not establish
human visual acceptance. No Steam/VM test is requested.

[Environmental survival](environment.md) adds deterministic Thermal/Electrical/Corrosive rooms and storm escalation. Combat,
new enemies/minigames, inventory, revive, perks, progression, persistence,
minimaps, procedural meshes and final environment art remain excluded.

## Third-person control and recovery

The owner camera orbits with mouse motion, starts eight meters behind at about
three meters above the mech, and zooms from 6.5 to 9.5 meters with the wheel.
Pitch is clamped from -0.7 to 0.12 radians; local ray collision pulls the camera
in around obstacles. WASD rotates by local camera yaw before entering the same
Vector2 rollback input. Camera state and authoritative body orientation remain
outside that input; Netfox still owns physical movement and GAS owns speed.
Tab releases the mouse and disables look while the world continues. Closing
Tab recaptures the mouse. Existing pause-menu behavior remains deferred.

Playable speed is max(3.5, canonical Mobility * 3), with absent mobility using
the same emergency floor. Pristine speed remains six; damage still slows a mech
above the floor. A brief EMERGENCY LOCOMOTION alert marks entry into the floor.
Generic non-player bodies do not gain locomotion.

Safe or dormant terrain supports recovery after 2.5 stationary, uninfected
seconds. Moving, infection or active hazard cancels recovery and resets its
wait immediately. Energy restores at one per second up to canonical capacity;
normal affordable cooling continues. Every 3.5 recovering seconds, the lowest
installed eligible module gains 0.05 condition, capped at 0.50. Eligible slots
are Sensor, both legs, Power and Cooling, including offline installed modules.
Ties follow that fixed order. Missing modules, Core, Torso and arms are excluded.
Exact instances/IDs stay installed; existing GAS reconciliation owns the result.
Full repair still requires salvage/replacement. See [environment](environment.md).

The deterministic layout/encounter stream and LayoutSignature are unchanged by
physical sector scaling. Required acceptance includes product tests and the
focused offline/local two-peer -RunLoop paths; detailed evidence stays in artifacts.
Human host + VM playtesting remains the final go/no-go judgment.
