# Random expedition v1

The normal ten-minute run explores a connected facility, salvages three machines,
and extracts every currently active mech. SurveyBeacon and the fixed corridor
are retired. Shell/player/disconnect/teardown ownership remains in GameFactory.

## Seed and layout

The server spawns one server-owned ExpeditionRun with an integer seed in spawn
initialization. Every peer synchronously generates its static collision geometry
from that seed before ExpeditionRun finishes entering the tree. Clients do not
choose seeds or advance run rules. Normal rounds choose a fresh random seed;
focused probes explicitly select fixed seeds. The HUD shows the seed.

ExpeditionLayout is a small pure generator with its own specified 32-bit LCG,
ordered frontier enumeration and SHA-256-derived 16-hex-digit layout signature.
Default size is 18 rooms (supported range 16–20), with 22-unit cardinal cells.
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
prop scatter. ExpeditionRun adds solid missing-exit walls or six-unit doorways
between neighboring room floors. Generated geometry belongs to the run object
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

HUD displays EXPEDITION, SALVAGE n / 3, extraction lock state, timer and seed.
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

Current validation: build and 144 product tests passed. Offline
`expedition_offline_20261020_04` and local two-peer
`expedition_multiplayer_20261020_02` passed with frozen sources/build and verified
process/profile/port cleanup. The client explicitly observed parasite motion and
target identity; extraction stayed active with only one of two players present.
Seed 120000 produced signature FF0CFD1B7C7D6F38 (18 rooms, 21 connections,
extraction nine graph edges from entry). Reset seed 654321 produced
241B92EEDECC3939 (18 rooms, 20 connections). Validation and preserved failed
attempt history live in `game/artifacts/run_loop/expedition_acceptance_20261020/summary.json`.

Task 14 keeps the geometry/encounter stream and LayoutSignature stable, adds a
separate EnvironmentSignature and replicates storm/per-body exposure through
ExpeditionRun's observation. Start/extraction stay Safe. See environment.md for
rates, damage ownership, composition with parasites and fresh-round cleanup.
