# Parasite v1 and co-op purge

The initial roster is the neutral SalvageMachine and this hostile Parasite.
The parasite is one concrete game-owned encounter, without combat or AI/minigame
frameworks. Normal [run content](run-loop.md) spawns one on the return route only after
objective activation, away from all current bodies. Focused parasite probes
explicitly create their own infection and loose-part fixtures.

Lurking stays at its spawn transform. On the server, an eligible live PlayableMech
within 1.6 units is selected in NetworkObjectId order. The parasite claims that
mech's single parasite resource contribution and becomes Attached. A source-owned
slot on MechGasComponent enforces at most one parasite per mech and makes stale
cleanup harmless. It is not a general status stack or modifier registry.

The parasite remains a separate server-owned NetworkWorld object with the same
NetworkObjectId and parent. AttachedNetworkObjectId is ordinary replicated state.
Every peer resolves it through the world and follows Presentation/ParasiteAnchor.
Presentation uses a pulsing sphere/material, world status/resource labels and a
purge-operator-only timing HUD. No attachment or minigame state enters rollback.

Attached and Purging add 0.2 Energy drain/second and 6 Heat/second through the
existing native Mech GAS resource step. External environment Heat remains separate
and additive. Each step clamps Energy to capacity, pays base drain, pays parasite
drain, adds both Heat loads, then performs affordable canonical cooling at its
existing Energy cost. Double intermediates preserve finite bounds. The server
alone advances resources; clients consume the existing canonical ASC snapshot
and resource tags. Installed capacities and unrelated native effects are intact.

E uses the existing PlayerInteractor/IInteractable path to start purge. Either
the infected owner or a nearby teammate can operate it, but only one at a time.
The explicit state flow is Lurking -> Attached -> Purging -> Expelled/despawn.
Purge timeout (12 seconds), operator disappearance/invalidity or moving more than
2.75 units from the infected mech aborts to Attached with a cleared operator,
cursor and progress. Resource pressure continues and the player can retry.
If the infected mech disappears, the parasite clears its claim and despawns.

This bespoke timing challenge sweeps cursor 0 through 7 every 0.35 seconds:

- Self purge: safe positions 3-4; three successful pulses.
- Teammate purge: safe positions 2-5; two successful pulses.
- `purge_pulse` is bound to T (R already has a project binding).
- A successful hit disarms that safe-window pass. The cursor must leave the safe
  zone before another hit can count. Pulses while disarmed cannot score.
- An armed pulse outside the window resets progress. There is no damage or
  permanent lockout; the pressure and bounded attempt are the cost of failure.

Only a pulse crosses the input RPC, with no cursor, timing or result payload.
The server derives attachment, operator, ownership, self/teammate policy, cursor,
window, arm state, progress, timeout and range. Forged/non-operator pulses and
concurrent interactions cannot take over. Host-local input calls the same
canonical method with PeerId.Server. Clients render replicated observations;
they cannot mutate resource loads, choose an infected target or claim success.

Success clears only the matching parasite contribution and challenge/attachment
state, then server-despawns the parasite. It never refills Energy, clears Heat,
resets GAS, rebuilds the ASC or removes unrelated effects. Normal cooling and
resource cadence handle the remaining state. No loot is emitted.

Tree exit clears the matching contribution even on ordinary despawn. GAS teardown
also clears its contribution fields. Explicit ResetResourceState clears all load
inputs; a live parasite whose claim was invalidated then despawns. Round cleanup
removes lurking/attached/purging parasites and clears timers/operators/load claims.
A new round constructs fresh parasite IDs in Lurking with no stale state.

Validation uses the existing product tests and runner:

```powershell
dotnet build GameFactory.sln --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test game/tests/MechSurvival.Tests/MechSurvival.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
./game/probes/run_playable_mech_probe.ps1 -Parasite -Mode Offline -RunId <unique_id>
./game/probes/run_playable_mech_probe.ps1 -Parasite -Mode Multiplayer -RunId <unique_id>
git diff --check
```

Evidence is under game/artifacts/parasite/. The focused path checks real automatic
attachment and motion following, native pressure/cadence/effects, self purge,
timeout and retry, both cooperative directions, client self purge, late attached
observation, forged inputs/load guards, one-parasite capacity and three-round
cleanup/freshness. Automatic resources are briefly enabled to prove cadence;
other comparisons use the existing probe pause/manual-step mechanism so removal
can be checked against exact current snapshots. Production keeps automatic steps.
No Steam/VM or manual visual acceptance is claimed.

Excluded: chase/pathfinding, additional species/enemies, physical combat, HP,
player/part damage, death/revive, perks/abilities, parasite loot, generic status
effects or minigame systems, additional minigames,
progression, persistence and final art/audio/UI.
