# Expedition environment

The expedition environment is one concrete game-owned policy/controller. It owns
room exposures and clocks, and calls existing GAS input and NetworkMechBody damage
boundaries. It never owns current Energy/Heat, mutable part condition, encounters,
players or world teardown. It stays outside rollback and client authority.

## Determinism and assignment

ExpeditionEnvironmentPolicy uses expedition seed XOR 0xE17A4B39 with its own fixed
LCG and ordered shuffle. Task 13 layout/encounter generation is untouched. The
normal 18-room plan contains eight Safe, four Thermal, three Electrical and three
Corrosive rooms. Entry and extraction are always Safe. First salvage placement is
Thermal and second is Corrosive, both initially active. An initially active
Electrical room also always exists. Remaining hazards activate at level 1 or 2.
Dormant zones stay visually subdued without large warnings. Safe sectors never activate.

EnvironmentSignature separately hashes room kinds/activation levels, phase
length, intensity, functional target set and environmental/resource tuning. Spawn
initialization carries the server-authored StormPhaseSeconds alongside the seed;
all peers reconstruct the same plan. LayoutSignature retains Task 13's meaning.
Storm level and authoritative per-body exposure/damage feedback are carried in
the existing run observation. Clients never advance the environment clock.

## Tuning

| Rule | Value |
| --- | --- |
| StormPhaseSeconds | 120 normal; authored on PrototypeRunContent |
| Calm / Unstable / Severe / Critical | elapsed 0 / 120 / 240 / 360 seconds |
| Intensity | 0.75 / 1.00 / 1.35 / 1.70 |
| Thermal input | 1.5 Heat/s times intensity |
| Electrical input | 0.08 Energy/s times intensity |
| Corrosion | one eligible installed part every 8 exposed seconds; 0.02 condition times intensity |
| Sustained overheat | Heat >=95 continuously for 6 seconds; one eligible part loses 0.02 condition |
| Expedition idle drain | 0 Energy/s |
| Cooling | existing scale 1 and cost 0.05 Energy per Heat removed |

Tuning lives in ExpeditionEnvironmentPolicy. Generic EnergyHeatSettings defaults
remain accepted. The normal 600-second run deadline remains the outer bound.

## Resource and structural ownership

A live body's current room comes from ExpeditionRun.RoomAt on the server.
SetEnvironmentalLoads replaces exactly one environmental Energy/Heat input.
Safe/dormant/terminal exposure clears environmental loads. Safe/dormant terrain
also permits the concrete recovery rule below; clearing loads itself never refills. Environment processing precedes the GAS resource cadence,
so room transitions affect the next resource step. Cooling still spends real
Energy and uses canonical condition-weighted CoolingCapacity.

Base, environmental and parasite Energy costs add before affordable cooling.
External/environmental and parasite Heat add before accepted saturation/cooling.
Purge clears only the parasite source. Room exit clears only environmental input.
The pre-existing explicit external Heat input remains separate. No setter assigns
current resources. Hacking and purge do not pause the production simulation.

Both corrosion and overheat select from functioning installed Sensor, LeftLeg,
RightLeg, Power and Cooling, skipping empty/destroyed parts. Selection uses seed,
body NetworkObjectId and a shared per-body damage pulse index. Core, Torso and
arms are excluded. ApplyDamage changes the exact installed PartInstance and keeps
its MechPartId, including when destroyed. Existing View/Changed reconciliation
updates Mobility, capacities, resource bounds, compact UI and visible modules.
No automatic detach occurs. A short temporary owner alert names the damaged
part without a normal-HUD percentage. Partial corrosion time clears outside active acid;
critical time clears below 95 Heat. High stored Heat can still damage a mech in
a Safe room until existing cooling actually lowers it. Catch-up is bounded to
one pulse of each kind per frame, without hidden damage debt.

## Presentation and cleanup

Thermal rooms have orange emissive floor/vent silhouettes; Electrical rooms have
cyan pulsing floor/pillars; Corrosive rooms have green translucent floor/volumes.
Active labels identify the immediate danger; dormant labels are hidden. None adds
navigation collision. Owner HUD shows one hazard chip, Energy/Heat bars and recent
short damage alerts. Storm increases show a temporary STORM INTENSIFYING message.
Existing service, hack and purge UI remains intact.

Despawn, disconnect, terminal completion and reset clear tracked environment
inputs and timers. Clearing never touches parasite claims, current resources or
installed parts. A new run constructs a fresh clock/plan and new body state.

## Validation and limits

Run build, product tests, the existing -RunLoop offline and local two-peer modes,
and diff check. Focused probes shorten authored storm phases to 20 seconds and
retain a bounded 1200-second process limit for real collision routes. They enable
automatic resources throughout environment travel, hacking and environment
purge; explicit resource boundary setups isolate electrical/overheat experiments,
and are not gameplay refill mechanisms. The diagnostic run overrides its deadline
to 1200 seconds for repeated cross-map visits; the production deadline stays 600. Historical parasite-only timing checks
remain paused before this live environmental sequence. Evidence and failed
attempt history stay under game/artifacts/run_loop/.

No new enemies/minigames, repair items, batteries, inventory, crafting,
death/revive, resistances, perks/progression, dynamic weather/lightning physics,
minimap or final art/audio/VFX. Human seed playtests determine go/no-go quality;
automated acceptance proves the concrete systemic behavior only.

## Concrete safe recovery

The server environment controller tracks one stationary timer per body. Safe or
currently dormant terrain, no parasite claim, horizontal velocity and recorded
movement magnitude <=0.1 qualify. After 2.5 seconds, the existing native ASC
receives one Energy/second, capped at current PowerCapacity. Normal cooling
continues and retains its real Energy cost. Movement, infection or an active
hazard immediately clears the stationary and stabilization timers.

Every 3.5 recovering seconds, one lowest-condition installed Sensor, LeftLeg,
RightLeg, Power or Cooling module gains 0.05 condition, never above 0.50. Fixed
slot order breaks ties. Destroyed installed modules qualify; empty slots do not.
Core, Torso and arms never stabilize. NetworkMechBody.SetCondition preserves the
exact PartInstance and MechPartId, publishes composition and reconciles GAS.
Clients only observe replicated resources, conditions and Recovering state.
Teardown/reset discards all recovery timing; the next run starts with fresh
conditions/resources and exposure. STABILIZING is a small normal-HUD state.
