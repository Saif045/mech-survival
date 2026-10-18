# Mech Survival product scope

`game/` owns this product's mechanics, configuration, probes, tests, and docs.
GameFactory remains the canonical upstream; preserve the `gamefactory` remote
and release ancestry. Do not permanently change `factory/`, vendored addons,
framework tests, or framework tools for product behavior.

Read [the mech body contract](docs/mech-body.md) for body/part work. Keep a
connected player distinct from its operated body. `NetworkObjectId` identifies
the spawned body; `MechPartId` identifies a runtime part. Neither represents an
account, peer, player, or persistent inventory item.

Read [field parts](docs/field-parts.md) for loose parts and carry. Shared authored
definitions live in `mech/definitions/`. A loose embodiment retains the exact
server PartInstance; clients receive only immutable identity/model/condition.
Pickup/drop preserves both part and network object IDs. Compose factory
CarryTarget/PlayerCarrier/PlayerInteractor with explicit game permissions and
presentation anchor; carry and semantic actions stay outside rollback.
Field service requests contain only a selected slot; the server derives carried
parts and transforms, validates ownership and body policy before mutation, and
transfers exact PartInstance references. Successful install despawns the incoming
loose embodiment; replacement spawns the outgoing instance. Keep service out of
rollback and leave canonical stat/resource consequences to existing Mech GAS.

Read [salvage machine](docs/salvage-machine.md) for the concrete neutral encounter.
The server owns its challenge, operator, timeout and actual reward instances;
clients send only chosen symbols and render observations. Release exact stored
parts once through LooseMechPart; failure loses unreleased rewards without player
damage. Keep encounter state outside rollback and avoid generic enemy/minigame
frameworks. World teardown must clear encounter timers and reward ownership.

Only `NetworkMechBody` calls the pure mutable rules in networked gameplay.
Consumers inspect its immutable `View`. Keep composition and condition out of
Netfox rollback. Do not add client installation RPCs without a concrete
server-side request validation contract.

Slot definitions are explicit; extend the enum, configured definition resources,
and rules tests together. Presentation is labels in a game-owned probe. Do not
add a generic inventory, ECS, stat bus, or persistence schema.

Physical composition, condition, normalized damage and effective structural
output belong to the mech structure model. Effective output is base contribution
times condition; the raw contribution API keeps its accepted meaning. Gameplay
attributes/effects/tags belong to Mech GAS; do not build duplicate
attribute systems between them. Read [the damage contract](docs/mech-damage.md)
for damage work.

Canonical gameplay attributes, tags, effects, future resources and abilities
belong to the game-owned Mech GAS composition. Clients consume the replicated
server GAS result and its local ASC mirror, not independently derive canonical
values from physical output. Keep interop inside the mech adapter/bridge; do not
attach the player-specific factory GAS component to mechs. See
[the Mech GAS contract](docs/mech-gas.md).

Energy and Heat are canonical server-owned Mech GAS resources. PowerCapacity
bounds Energy; CoolingCapacity determines cooling rate. Environment/activity
systems provide load inputs, not resource state. Current Energy stays mech-level
until field-part gameplay supplies evidence for per-part stored charge. Fresh
initialization is explicit, never an installation/repair refill. See
[the resource contract](docs/energy-heat.md). Keep resources outside rollback.

Damage formulas, repair, environment and extra activity consumption,
survival, salvage, rescue, combat, extraction, and
persistence remain undecided. Do not implement them as part of this foundation.
Preserve accepted GameFactory layers; reopen them only for a scoped reproduced
problem with the routed upstream repair workflow.

The normal player prefab is the game-owned playable mech, composed through the
PlayerScene seam and configured gameplay routing. See [consumer migration](docs/v0.2-consumer-migration.md) for game-owned shell, spawn and GAS resource composition.
Read [the playable contract](docs/playable-mech.md)
for player/input/movement work. PlayerLifecycle tracks the initial operated body;
player identity remains distinct from body identity. Generic mech bodies stay
empty; complete loadout and explicit Energy initialization are playable policy.
Only deterministic latency-sensitive physical inputs belong in Netfox rollback.
Discrete actions use semantic InputMap actions and mechanic-specific validated
server requests; never read physical keys or place arbitrary buttons in rollback.
Playable locomotion consumes final canonical Mech GAS Mobility. Physical parts
determine structural baseline; GAS owns final gameplay value; Netfox owns only
physical movement. Current tunable conversion is MoveSpeed = Mobility * 3, hard
gated to zero by HasMobility. Keep GAS/condition/capability out of rollback;
movement must not independently calculate leg output or mutate GAS. Jump and
resource costs remain separate. Keep camera/color outside rollback.

Build and test commands are in the contract. Every runtime attempt follows
`factory/docs/testing-protocol.md` and `factory/docs/runtime-test-operator.md`. After a terminal
failure, follow `factory/docs/investigation-protocol.md`; retries need explicit approval.
Required runtime acceptance must pass before committing this layer.

Agent-launched Godot validation uses a disposable writable `APPDATA` profile.
The game-owned runner restores `APPDATA` and deletes its validated temporary
profile after every run, including failures. Request command-level execution
permission when Godot needs access outside the sandbox; do not require permanent
full-access mode or change project/runtime source to bypass agent restrictions.
Use the normal user profile only for a test explicitly reproducing the interactive
desktop environment. The unrestricted normal-profile import passed; prior
restricted-agent startup failures are environment failures, not project defects.

Task 01 validation: build, 20 product tests, and 73 foundation tests passed.
Offline `mech_offline_guard_20261012_024557` and local two-peer replication
`mech_multiplayer_20261012_024607` passed, with zero owned processes after cleanup,
profile deletion, APPDATA restoration and unchanged source/build. This body
foundation is acceptance-proven on that scripted local Godot/ENet boundary;
see the contract for exact assertions and preserved earlier failure evidence.

Task 02 damage validation: build, 41 product cases and 73 foundation cases passed.
Offline `mech_damage_offline_20261012_031922` and eight-stage two-peer
`mech_damage_multiplayer_20261012_031935` passed, including damaged late join,
effective-output matching, 64 client rejections, reset/despawn/fresh state and
process/profile/port cleanup. See the damage contract for evidence. Keep this
accepted physical layer separate from Task 03's future canonical Mech GAS state.

Task 03 Mech GAS validation: build, 50 product cases and 73 foundation cases passed.
Offline `mech_gas_teardown_offline_20261012_035827` and twelve-stage two-peer
`mech_gas_matcher_multiplayer_20261012_040302` passed using the actual vendored ASC,
canonical snapshots/mirrors, additive effect coexistence, damaged/modified late
sync, native tags, 144 client rejections and full cleanup. Earlier probe teardown
and log-parser failures are preserved in the GAS contract. Source-owned baseline
effect replacement must preserve unrelated native effects; do not reset the ASC
when physical structure changes. Resource updates must likewise preserve
unrelated native effects. The resource contract records Task 04 acceptance.
