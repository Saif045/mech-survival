# First complete run

The normal game uses PrototypeRunContent, one game-owned IRoundContent with a
120-second authored DurationSeconds. GameFactory still owns players, NetworkWorld,
round teardown and lobby/session reuse. No mission or encounter framework exists.

Four authored spawn positions surround START / EXTRACTION at z=+8. A nine-unit
wide primitive route leads to the survey beacon at z=-18. Low-cost walls and
a crate define the route; the optional SALVAGE bay branches west at (-7,-1).
Landmark labels show the destination, optional reward and return point.
The start marker has a 4.5-unit extraction radius (and a generous height bound).

The server advances a finite deadline and explicit phases: RunningToObjective,
ReturningToExtraction, Success, Failure. SurveyBeacon is a server-owned
NetworkObject using IInteractable and normal PlayerInteractor/E validation.
The first valid activation completes the objective and unlocks extraction;
repeated or terminal interactions do nothing. Any player may activate it.
A single replicated beacon observation carries phase, rounded-up remaining
seconds, objective flag and inside/active counts. Clients display observations.

Salvage is optional and costs travel plus the unchanged memory challenge.
The machine stores exact industrial_cooling at 90% and high_density_power at
85%. Their effective capacities are 7.2 Cooling and 12.75 Power versus pristine
starter values 5 and 10. E picks up a reward; J selects the Cooling/Power slot,
K installs it through ordinary field service, and Q drops a carried part.
Installation preserves current Energy/Heat and exact part identities; capacity
increases never refill Energy. Failure destroys the unreleased opportunity.
There are no free development parts beside spawn.

Only objective activation spawns the accepted Task 10 Parasite on the return
route. Authored candidate sites are checked against all bodies so it appears at
least 2.75 units away. Five sites six units apart guarantee a clear site for
the intended maximum of four players. Proximity causes its existing canonical drain/Heat load.
E starts self or teammate purge; T pulses the unchanged timing challenge.
Improved cooling naturally mitigates Heat through existing GAS rules. Neither
the run nor beacon calculates attributes or resources.

Extraction is locked before activation. After activation, every current live
PlayableMech in NetworkWorld must be inside the marker. One of two players
cannot finish alone; zero bodies cannot succeed. Existing disconnect lifecycle
removes departed bodies from this requirement. Deadline advancement happens
before extraction checks, so expiration wins on the deadline frame.
Success and Failure are terminal and immutable.

An owner-visible run HUD shows TIME, objective instructions, extraction counts,
then RUN COMPLETE or RUN FAILED — TIME EXPIRED. After roughly two seconds the
server requests OnlineGameplayShell.ReturnToLobbyAsync once. The existing shell
clears the world and returns the retained session to its lobby. A fresh round
gets fresh players, beacon, machine and reward instances, full timer, locked
extraction and no parasite. NetworkWorld object IDs remain monotonic.

The local focused harness extracts the production world outside the Steam shell.
It uses the existing direct ResetRoundAsync lifecycle to prove resets and session
reuse, and does not pretend to validate Steam or a human playtest.

Validation:

- dotnet build GameFactory.sln
- dotnet test game/tests/MechSurvival.Tests/MechSurvival.Tests.csproj
- game/probes/run_playable_mech_probe.ps1 -RunLoop -Mode Offline -RunId <unique>
- game/probes/run_playable_mech_probe.ps1 -RunLoop -Mode Multiplayer -RunId <unique>
- git diff --check

Evidence lives under game/artifacts/run_loop/<run_id>. The focused route covers
normal initial content, real movement/interactions, exact useful reward transfer,
native capacity increase without refill, objective once, delayed parasite,
self/team purge, all-active extraction, immutable success/failure, and clean
same-session resets/fresh identities. Scripted evidence is not visual acceptance.

Current exclusions: combat, death/revive, additional enemies or minigames, perks,
abilities, progression/persistence, inventory/crafting/economy, procedural maps,
random encounters, matchmaking/dedicated-server changes and final art/audio/UI.
The next step is a real host + VM/Steam playtest, not another system task.

## Acceptance evidence (2026-10-19)

Final build: GameFactory.sln passed with no compiler errors. Restore reported
NU1900 for unavailable NuGet vulnerability data in the unchanged factory test
project. All 127 product cases passed; foundation tests were not run.
git diff --check passed. No factory, vendor or transport source was changed.

- Offline: game/artifacts/run_loop/run_loop_offline_20261019_04/ — PASS.
  Real movement and salvage interaction released the exact stored rewards once;
  Cooling installation produced 7.2 from 5 while Energy 6 / Heat 12 remained
  unchanged. Client-free objective interaction completed once; only then did a
  parasite appear. Its real native step produced Energy 6 -> 3.9 and Heat
  12 -> 11.8 with improved cooling and parasite drain 0.2. Self purge, extraction,
  immutable timed failure and fresh third-round content all passed.
- Local two-peer: game/artifacts/run_loop/run_loop_multiplayer_20261019_04/ — PASS.
  Ten client observations converged for timer/phase, body/GAS, salvage/rewards,
  objective, infection/removal and extraction. Client activated the beacon and
  performed teammate purge on the host. Host entering alone retained the active
  run at 1/2; client arrival produced Success on both peers. Client canonical
  result publication was rejected. Short failure and fresh identities/state
  passed through the existing direct round lifecycle in the same ENet session.
- Both final attempts preserved frozen source/build hashes and reported zero
  owned processes/objects after cleanup, removed temporary profiles and restored
  APPDATA. The multiplayer port was released. This is scripted local acceptance;
  the Steam shell return is integrated through its existing API and remains
  part of the separately planned human host + VM/Steam playtest.

Preserved earlier attempts:
- run_loop_offline_20261019_01 — FAIL at beacon creation: missing scene UID;
  restricted execution also logged certificate-store access failure.
- run_loop_import_20261019_02 — PASS after adding the scene UID and using
  command-level execution outside the sandbox with disposable APPDATA.
- run_loop_offline_20261019_02 — FAIL after salvage success: scripted diagonal
  movement became blocked between the machine and released Power reward.
  Explicit approach/exit waypoints repaired the probe route.
- run_loop_offline_20261019_03 and run_loop_multiplayer_20261019_03 — PASS.
  Final attempts above additionally cover the reviewed four-player-safe
  candidate placement and explicit client run-publication rejection.
Every failed runtime attempt was cleaned up and followed by explicit user
authorization before repair/retry. No normal-profile or production startup
workaround was introduced.
