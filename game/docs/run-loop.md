# Prototype run / playability pass

PrototypeRunContent is the single game-owned IRoundContent. The normal authored
DurationSeconds is 600 (ten minutes), deliberately generous for controls,
navigation and equipment playtesting. Pure run rules contain no 600-second
assumption. GameFactory still owns players, NetworkWorld, teardown and retained
lobby/session reuse. No mission or encounter framework is introduced.

## Authored route

Four player starts surround extraction at (0, 0, +8). The beacon is at
(0, 1, -100): 108 units between landmarks, roughly four times the original
26-unit route. Primitive floor sections form a start yard, crate yard around
z=-22, narrow long connector around z=-48, industrial yard around z=-73 and
objective facility at the far end. The whole blockout reaches z=-106 to +15
and roughly x=-15 to +15, with varying widths, walls and crates. The main
centerline stays traversable. Labels and colored floors distinguish spaces.

Three optional SalvageMachines occupy separate side bays:
A (-7, 1, -1), B (10, 1, -31), C (-10, 1, -73).
Each uses the same memory minigame and exact stored industrial_cooling 90% /
high_density_power 85% rewards. Effective capacities remain 7.2 Cooling and
12.75 Power versus pristine starter values 5 and 10. There are no free debug
parts, loot tables or inventories. Failed salvage loses that machine's rewards,
leaving other machines available.

## Mech status and explicit service

E interacts/picks up; Q drops; Tab toggles the owner-only Mech Status / Service
panel. All nine slots show their installed model, condition and physical
contribution/capabilities, or EMPTY. Select a slot with its row button. The
carried part is compared against that slot, including condition-weighted
contribution differences. No future GAS modifier prediction is attempted.

INSTALL / REPLACE always requires a carried compatible LooseMechPart. Missing
carry cannot turn it into a detach. DETACH deliberately removes the selected
installed module and never installs a carried part. Server-derived instances,
identity, ownership validation, outgoing loose parts and failure recovery are
unchanged. Higher capacity never refills Energy. J/K are removed from normal
controls. A compact owner-only status list remains when the panel closes,
including missing slots, carried model/condition and the Tab hint.

Every physical slot also has its own primitive module. Missing slots disappear;
condition reduces brightness/emission. Functional categories have distinct colors
and spatially separated limbs, while a dedicated owner-colored marker preserves
player identity. Visibility/condition observe NetworkMechBody.View and Changed;
there is no presentation-owned equipment state or final mesh/animation work.

## Objective, return and extraction

The HUD says PRIMARY OBJECTIVE: Activate the Survey Beacon and extraction stays
locked until activation. The beacon has an obvious emissive marker and world
label. Normal E selection uses the existing interactable group and server
PlayerInteractor validation. The first valid activation completes it once.

Activation announces for five seconds:
SURVEY COMPLETE / EXTRACTION UNLOCKED / PARASITE ACTIVITY DETECTED / RETURN TO START.
The normal HUD then says OBJECTIVE COMPLETE / RETURN TO EXTRACTION.

Only then are two accepted Task 10 Parasites spawned in separate return zones.
Each zone has five candidate positions six units apart: z=-80 through -56,
and z=-44 through -20. Four bodies cannot occupy every candidate's exclusion
radius; sites are at least 2.75 units from players and the zones stay separated.
Their brighter primitive silhouettes and labels improve discoverability.
Attachment, one parasite load per mech, canonical resource pressure and self/team
timing purge are unchanged. E starts purge; T pulses the challenge.

The start marker changes from orange locked to green active. Success requires
the objective and every live active PlayableMech inside the generous 4.5-unit
extraction radius. One of two players cannot finish alone; zero bodies cannot
succeed. Existing disconnect lifecycle removes departed bodies. Deadline
advancement precedes extraction, and Success/Failure remain immutable.

After roughly two seconds of RUN COMPLETE or RUN FAILED, the server requests
OnlineGameplayShell.ReturnToLobbyAsync once. The existing shell resets the world
and returns the retained session to its lobby. New rounds have fresh players,
objective, three machines and reward instances, full timer, locked extraction
and no parasites. Normal monotonic object IDs are preserved.

## Encounter feedback and deferred issues

Hack failure displays HACK FAILED, REWARDS LOST and SELF DESTRUCT 3...2...1...
with red emissive flashing. Zero displays SALVAGE DESTROYED / REWARDS LOST and
briefly expands/flashes the existing primitive before despawn. There is no
explosion damage, sound, new minigame or persistent effect node.

The service panel never pauses multiplayer. The existing Escape/pause menu
still pauses the authoritative host tree, stopping the run timer for both peers;
client pause affects that client locally. This known online UX issue is deferred.
Inventory, stored items, perks/abilities, new enemies/minigames, combat,
death/revive, progression, persistence, procedural maps and final art/UI remain
excluded.

## Focused validation

Build GameFactory.sln, run the product test project, then the existing
run_playable_mech_probe.ps1 with -RunLoop -Mode Offline and -Mode Multiplayer.
Run git diff --check. No foundation suite, historical-mode sweep or automatic
Steam/VM acceptance is required.

The focused scenario uses explicit service intents, Tab without pause, exact arm
detach/reinstall and both peers' immutable View observations, useful salvage
success/installation, one failed machine's countdown/reward loss, the full
expanded movement route, real E beacon activation, two post-objective parasites,
self/team purge, extraction, short immutable failure and fresh reset.
The larger route has a defined 240-second process bound. Local probes extract
the production world outside the real Steam shell and use its existing direct
reset lifecycle. Evidence is under game/artifacts/run_loop/<run_id>.
Headless acceptance does not test pixel colors or claim human visual acceptance.

Current acceptance: build, 133 product cases, offline playability_offline_20261019_03
and local two-peer playability_multiplayer_20261019_03 passed. Detailed results,
cleanup and preserved failure/repair evidence are in
game/artifacts/run_loop/playability_acceptance_20261019/summary.json.
