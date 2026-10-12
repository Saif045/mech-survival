# Mech Survival product scope

`game/` owns this product's mechanics, configuration, probes, tests, and docs.
GameFactory remains the canonical upstream; preserve the `gamefactory` remote
and release ancestry. Do not permanently change `factory/`, vendored addons,
framework tests, or framework tools for product behavior.

Read [the mech body contract](docs/mech-body.md) for body/part work. Keep a
connected player distinct from its operated body. `NetworkObjectId` identifies
the spawned body; `MechPartId` identifies a runtime part. Neither represents an
account, peer, player, or persistent inventory item.

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
attributes/effects/tags belong to the later Mech GAS layer; do not build duplicate
attribute systems between them. Read [the damage contract](docs/mech-damage.md)
for damage work.

Damage formulas, repair, heat, energy consumption,
movement degradation, survival, salvage, rescue, combat, extraction, and
persistence remain undecided. Do not implement them as part of this foundation.
Preserve accepted GameFactory layers; reopen them only for a scoped reproduced
problem with the routed upstream repair workflow.

Build and test commands are in the contract. Every runtime attempt follows
`docs/testing-protocol.md` and `docs/runtime-test-operator.md`. After a terminal
failure, follow `docs/investigation-protocol.md`; retries need explicit approval.
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
