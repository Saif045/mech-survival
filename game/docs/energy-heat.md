# Networked Energy + Heat

Energy and Heat are canonical native GodotGAS attributes on each mech's existing
ASC. Current Energy belongs to the mech, not a Power part/battery. Physical parts
still own their condition and structural contribution; GAS capacities remain the
effective baseline plus native effects. No per-part charge, transfer or persistence
semantics are introduced.

## Rules and inputs

- Max Energy is current canonical GAS PowerCapacity. Lower capacity clamps Energy
  immediately; higher capacity does not refill it. Zero capacity forces zero Energy.
- `InitializeEnergyFull()` is server-only, explicit and once per body instance. It
  sets Energy to current capacity while preserving Heat. Install/repair never
  auto-initializes; repeated calls return false. Invoke after configuring a fresh
  body. Resources otherwise start depleted/cold.
- Heat is bounded to `[0,100]`. Server `SetExternalHeatLoad` supplies nonnegative
  heat units/second; no environment or activity system exists yet.
- Cooling rate is canonical CoolingCapacity times CoolingRateScale. Cooling is
  limited by available Heat and Energy; actual heat removed times
  CoolingEnergyCostPerHeat is charged. Cost must be finite and strictly positive.
  Zero Energy, zero cooling, or zero Heat prevents cooling cost.
- Idle drain is BaseEnergyDrainPerSecond. Defaults are 0.1 Energy/second,
  CoolingRateScale 1 and CoolingEnergyCostPerHeat 0.25; these are tunable values.
- Native `State.PowerDepleted` exists at Energy `<=0`; `State.ThermalCritical`
  exists at Heat `>=100`. No subjective threshold, penalty, damage or death exists.

`EnergyHeatPolicy` is pure arithmetic; it never stores authoritative current
resources. Each step clamps Energy to capacity, spends idle drain, adds external
Heat, determines affordable thermal work, spends its Energy, removes Heat and
clamps the result. Incoming Heat saturates at 100 **before** cooling; excess load
is not stored as hidden thermal debt. This deliberate ordering charges work on
represented Heat, and allows cooling to lower a previously maximum Heat state.
Double intermediates avoid overflow for finite float-sized inputs; results use
float values without display rounding. NaN/infinite/negative inputs reject;
Heat setup above 100 and zero-cost cooling configuration also reject.

## Authority, cadence and GAS

Only server `MechGasComponent` advances resources. Each step reads actual native
ASC Energy/Heat and canonical capacities, computes a result, and applies focused
native instant attribute overrides through the existing adapter/bridge. The ASC
is never rebuilt. Structural baseline reconciliation and native capacity changes
reconcile resource bounds before capture. Independent Mobility effects and
capability tags remain intact. Resource tags have their own native reference
ownership and are derived from the applied ASC values, not cached C# resources.

Automatic simulation defaults on for servers at fixed 0.25-second steps. Accumulated
time is capped at one second/four steps per rendered frame; excessive catch-up
time is dropped deliberately. Clients never run this simulation. A changed result
publishes the extended atomic MechGasSnapshot (Energy, Heat, PowerDepleted and
ThermalCritical alongside accepted fields). Equal stable state emits no resource
mutation or snapshot churn. Canonical client ASC mirrors apply server values/tags;
they do not reconstruct resources from capacities or wall-clock time.

`ResetResourceState()` clears resources to depleted/cold, input load and pending
time while preserving unrelated effects and structural state. It does not reopen
one-time initialization. Physical ResetBody remains a structural operation; a
run/test reset calls both deliberately. Despawn removes the whole composition,
clearing native attributes/tags/effects and cached state. A new mech owns fresh
resources, settings and initialization eligibility.

## Probe and validation

The existing probe adds explicit initialization, heat-load presets, manual steps,
run/pause and development-only Heat setup. It displays capacities, canonical
resources/tags and server rate inputs. `SetResourcesForProbe` is internal,
authority-checked boundary setup, not gameplay recharge or repair.

Scripted bodies normally pause automatic simulation so network assertions compare
the server's published sample with the received native mirror, not two clocks.
Offline acceptance separately enables automatic simulation and waits for an
observable native Energy decrease under a bounded timeout. It also covers full
initialization, actual cooling cost, zero-load cooling, damaged capacities/rates,
hard tags, restored boundary setup, modifier coexistence and stable no-churn.

The existing twelve-stage multiplayer progression retains GAS/damage regressions
and adds non-default resource late sync, server steps/load changes, lower cooling,
immediate Power-damage clamping, no recharge on increased capacity, depletion and
thermal maximum, reset and fresh explicit initialization. Nineteen client mutation
paths per stage reject, including resource initialization/setup, load, advancement,
configuration, enabling simulation, reset and arbitrary snapshot publication.
All assertions read the real ASC through the typed boundary. No extra replicated
resource channel exists. Teardown retains the accepted world-before-peer ordering
and multi-digit log-stage parsing.

Use existing build/product/foundation commands and the game-owned Offline/
Multiplayer runner. Each attempt freezes source/build, uses a disposable APPDATA
profile with command-level permission when needed, preserves evidence, restores
APPDATA, removes the profile, verifies zero processes/objects and releases the port.
Task 04 acceptance passed on 2026-10-12:

- `dotnet build GameFactory.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false`:
  zero warnings/errors. Product tests: 67 passed; foundation tests: 73 passed,
  zero failures/skips. Logs: `artifacts/mech_body/mech_resources_units_20261012_044440/`.
  PowerShell parsing and
  `git diff --check` passed.
- Offline: `artifacts/mech_body/mech_resources_offline_20261012_044459/`.
  Native ASC resource mutation, actual cooling cost, capacity/rate damage,
  hard-tag transitions, automatic server cadence, modifier preservation and
  stable no-churn assertions passed.
- Multiplayer: `artifacts/mech_body/mech_resources_multiplayer_20261012_044510/`.
  All twelve stages passed with native client mirrors and 228 rejected mutations.
  Late sync received Energy 8.25 / Heat 1.5. Power damage clamped Energy to 5;
  the next step under damaged cooling produced Energy 4 / Heat 6. Depletion/
  thermal maximum converged at 0 / 100, reset at 0 / 0, and fresh initialization
  at 10 / 0. Independent Mobility +0.5 survived resource operations.
- Both runtime results report unchanged source/build, restored APPDATA, removed
  temporary profiles and zero owned processes. Multiplayer port 24873 was
  released. No runtime errors or transport-specific blocker occurred.

Evidence is from the focused scripted local Godot/ENet boundary, not a Steam/VM
or human gameplay session. The modular body, damage, native Mech GAS and resource
foundation cluster is complete; later gameplay layers remain separate tasks.

## Deferred

No environment/weather/sun zones, movement consumption or penalties, shutdown,
sensor consequences, heat damage, death/rescue, repair, field swapping, battery
charge persistence/transfer, hazards, salvage, extraction, economy, combat or final
UI/audio/VFX. The resource state is ready for future consumers, not their behavior.
