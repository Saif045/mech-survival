# Reusable gameplay scope

Use [interaction](interaction/AGENTS.md) for target requests and
[carry](carry/AGENTS.md) for holder/item composition. For starter inventory,
vitals, equipment or revive go to [starter](../starter/AGENTS.md).

- Actor/target policy belongs to the game; reusable mechanisms cannot depend
  on starter players, GAS, inventory or product semantics.
- Discrete requests use validated reliable RPC; ordinary authoritative state
  uses Godot synchronization. Continuous physical simulation uses Netfox.
- Client availability is a hint; server canonical validation decides.
- Keep cosmetic presentation and discrete interaction/carry state outside rollback.
- Read architecture only for cross-module boundary changes and current-state
  only for maturity or evidence claims.
