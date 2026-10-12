# Functional mech damage

## Structural contract

Damage extends the accepted body model without changing identity, slots,
attachment policy, immutable observations or the atomic `Composition` snapshot.
Condition remains finite and bounded to `[0, 1]`. `ApplyDamage(slot, amount)`
uses normalized condition space: `1 - 0.25 = 0.75`; excessive damage saturates at
zero. Negative, NaN and infinite requests throw before mutation, including for
empty slots. Unknown slots reject. Damage never detaches, replaces, cascades to
other slots or creates global mech HP.

`PartDamageResult` reports nullable part ID, slot, previous/new condition,
requested/applied damage, whether condition changed, and whether this operation
destroyed the part. Empty slots return a null ID, zero applied damage and no
change. Zero damage and damage to an already destroyed part are no-ops. A tiny
request that cannot change the represented float also returns no change. Only a
changed result publishes a new server composition; no-op/invalid requests do not
emit body Changed notifications or unnecessary replication.

The raw query `SumContribution(stat)` retains its original meaning, including
destroyed installed parts. New queries are:

- `InstalledPart.GetContribution(stat)`: configured base contribution.
- `InstalledPart.GetEffectiveContribution(stat)`: base times condition.
- `MechBodyView.SumEffectiveContribution(stat)`: sum of effective installed output.

Example: two legs with base Mobility 2 and conditions 0.5/1 have raw output 4
and effective output 3. At condition zero a part remains installed with its ID
and raw contribution, supplies zero effective output and no functioning
capabilities. Any positive condition supplies its configured capabilities; no
capability thresholds or condition bands exist. Parts contribute independently.
Captured immutable views never change retroactively. Projection uses float
arithmetic without authoritative rounding; tests compare expected arithmetic
with an appropriate tolerance.

## Authority and next-layer boundary

Only server peer 1 can call the network body mutation API. Clients derive raw,
effective and capability queries from the existing condition-bearing snapshot.
No damage commands/results, extra attributes or per-part objects are replicated.
No damage state enters Netfox rollback, and no client damage request RPC exists.

This is physical structural output, not canonical gameplay state. Task 03's
game-owned Mech GAS will consume `MechBodyView` and own gameplay attributes,
effects, tags, resources, temporary modifiers and abilities. No GAS integration
or duplicate attribute system is added here. There are no global HP, core-death,
repair, armor, resistance, damage-type, dependency-graph or movement rules.
`SetCondition` remains authoritative probe/setup functionality, not repair.
Energy/Heat and later survival, salvage, rescue, hazards, persistence, combat,
inventory interaction and final presentation remain deferred.

## Probe and acceptance

The existing probe now displays full runtime IDs, condition, functioning status
and raw/effective contributions per stat, plus aggregate output. Scrollable debug
controls apply 0.10/0.25 damage or destroy the selected part. No final UI or art
is introduced. Build/tests and the disposable-profile runner remain those in
[the body contract](mech-body.md).

Offline assertions cover pristine output, partial damage, raw preservation,
linear effective output, saturation at zero, retained destroyed parts,
capability loss, unrelated-part stability, captured views, invalid requests,
empty targets and absence of publication for no-op damage.

Two local Godot/ENet peers compare actual replicated snapshots and independently
derived raw/effective output plus all capability counts at eight stages:

1. Late join receives an already damaged leg near condition 0.35.
2. Server damage 0.10 leaves condition near 0.25.
3. Replacement uses a new runtime ID and condition 0.75.
4. Damage destroys the installed part, retaining raw output and losing capability.
5. Detach clears the slot.
6. Both peers observe a damaged leg and sensor before reset.
7. Reset clears that previously observed damage/composition.
8. Despawn and a fresh body provide a pristine leg with matching full output.

At every stage the client attempts creation, attach, replace, detach, condition
restoration, damage, reset and world spawn; all eight must reject while server
state remains unchanged. Test RPCs carry assertion expectations/acknowledgements
only; all authoritative condition state travels through existing replication.
Client disconnect and server teardown must clear objects and cached views.
The runner checks zero owned processes, released port 24873, profile removal,
restored APPDATA and frozen source/build hashes. No Steam/VM A/B is required.

## Accepted evidence

- `dotnet build GameFactory.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false`:
  passed with zero warnings/errors.
- Product suite: 41 passed (20 accepted body cases plus 21 damage/projection
  cases). Foundation suite: 73 passed. Both used
  `dotnet test <project> --disable-build-servers -m:1 -p:UseSharedCompilation=false`.
  Logs: `artifacts/mech_body/mech_damage_units_20261012_031852/`.
- Offline **PASS**: `mech_damage_offline_20261012_031922`, PID 27152,
  `MECH_DAMAGE_OFFLINE_PASS`, `MECH_OFFLINE_PASS` and zero world objects.
  All offline assertions above completed with no engine/runtime errors.
- Multiplayer **PASS**: `mech_damage_multiplayer_20261012_031935`, host PID
  22008/client PID 21460. Eight actual composition/structural projections matched;
  64 client mutations rejected. Late join received condition `0.35000002`,
  subsequent damage yielded `0.25000003`, and both peers derived identical output.
  Destruction retained raw Mobility 1 with effective output 0/capability count 0.
  Reset, despawn, fresh pristine state and disconnect cleanup passed.
- Both attempts preserved frozen source/build hashes, restored APPDATA, deleted
  temporary profiles and verified zero owned processes/objects. Multiplayer port
  24873 was released. Managed assembly SHA-256:
  `687C2BB93E403D56EF87ED14868E699E11BF60DD0848F394A218983E90DAEC6E`.
  Their `contract.json`, `result.json`, frozen hashes and stdout/stderr/engine logs
  are preserved in `artifacts/mech_body/<run_id>/`. Command-level execution
  permission was used; no normal user profile, VM or Steam session was used.
- `git diff --check` and the game-owned runner's PowerShell parser check passed.

This accepts the structural damage contract on the scripted local Godot/ENet
boundary. Task 03 can consume these immutable projections without implementing
another physical condition or effective-output model.
