# Salvage machine v1

The normal game-owned PrototypeRunContent spawns five neutral SalvageMachines
in distinct randomized rooms; see [expedition](expedition.md). Three successful
Disabled machines unlock extraction. It uses
the same NetworkWorld and released factory PlayerInteractor/IInteractable path.
Primitive geometry, a reward/status Label3D, an operator-only HUD and a warning
flash provide prototype presentation. There is no combat or AI framework.

The explicit server-owned states are Neutral → Hacking → Disabled on success,
or Hacking → SelfDestructing → despawn on failure. Neutral advertises fixed
industrial_cooling condition 0.9 and high_density_power condition 0.85 rewards. It has no HP,
attacks, player damage or automatic hostility. Interaction voluntarily starts it.

The accepted interaction records the operator's NetworkObjectId. Only one
operator can own a machine's active challenge. The server generates four random
symbols in [1,4]. The operator sees them for 1.5 seconds, then enters them with
semantic actions hack_1…hack_4 (number keys 1…4) during an 8-second response
window. Preview inputs are rejected. Correct inputs advance progress; the fourth
completes the challenge once. A wrong valid symbol fails. Inputs outside [1,4]
and inputs after termination are rejected without progress. The challenge is a
single small concrete type, not a generic minigame system.

Each reliable owner-to-server input contains only a symbol. The machine derives
its state, expected symbol, progress, operator object, represented owner and
range from server state. Every input and server process checks that the operator
still exists, is a live PlayableMech in the same world and stays within 2.75 units.
The RPC validates its actual remote sender against the operator's OwnerPeerId;
host-local input uses PeerId.Server through the same canonical method. Another
player cannot hijack the machine or submit the operator's input. Clients cannot
claim success, sequence, progress, rewards, condition, transforms or state.

EncounterState is one replicated observation: state, operator ID, sequence,
progress and input-open phase. It stays outside Netfox rollback. Clients render
the observation and never advance the challenge independently. The sequence is
replicated prototype data, not a cryptographic secret; the operator HUD hides it
after the preview. Other players see machine progress, not the sequence HUD.

Trusted server creation constructs two actual PartInstances from the canonical
mech definitions. The machine owns those exact references until release or loss.
Spawn data gives clients only immutable model/condition reward previews and a
Godot world transform. GetServerRewards is internal and server-guarded; no client
PartInstance or arbitrary part-ID deserialization path exists.

Success enters terminal Disabled before release. Each stored instance is passed
directly to LooseMechPart.Spawn with a server-computed nearby transform. Its
MechPartId and object reference survive. The machine then drops its stored
references and remains an inert, non-interactable shell. Queued/repeated inputs
cannot create more rewards. The ordinary carry and field-service mechanisms can
install/replace the released parts; canonical body/GAS consequences remain there.
If release spawning unexpectedly throws, already-created embodiments are
despawned and the encounter fails without retrying or fabricating rewards.

Wrong symbol, response timeout, operator disappearance/invalidity or leaving
range triggers SelfDestructing, discards unreleased rewards and clears the
challenge/operator. A three-second HACK FAILED / SELF DESTRUCT countdown flashes red and
announces REWARDS LOST, followed by a brief expanding white flash and
SALVAGE DESTROYED message before server despawn. It destroys only the salvage opportunity and never damages players.

NetworkWorld owns round removal of machines, shells and loose rewards. Tree exit
unsubscribes replication and clears stored references and local timer values;
there are no asynchronous timer callbacks to survive teardown. Fresh rounds
construct new machines and new reward PartInstances/MechPartIds in Neutral with
no operator, sequence, progress or active timer. Nothing persists across rounds.

Validation extends the existing runner with `-Salvage -Mode Offline` and
`-Salvage -Mode Multiplayer`, with unique RunIds and evidence in
`game/artifacts/playable_mech/`. The focused path checks real interactions,
host/client challenge input and observations, exact reward transfer and normal
carry/install, invalid/forged/duplicate rejection, wrong/timeout/range failures,
all-state round cleanup and fresh identities. It uses disposable profiles and
local ENet, without Steam/VM or a manual visual acceptance claim.

Excluded from this encounter: chase AI, combat/weapons/HP/damage, explosion damage,
revive, perks/abilities, rarity/loot tables, additional minigames, generic enemy
or encounter systems, procedural levels, progression, persistence,
and final art/audio/UI.

The useful rewards yield 7.2 effective Cooling and 12.75 Power versus pristine
starter values 5 and 10. Installation never refills resources.
