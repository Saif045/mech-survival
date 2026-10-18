# Shell scope

For round lifecycle/composition, read [README.md](README.md), the canonical
shell/world contract and migration guide. Topic documentation stays beside its
subsystem; agent context stays narrow even when repository knowledge is extensive.
Read cross-boundary material only when the task actually crosses that boundary.

- Spawning internals/identity: [networking guide](../networking/AGENTS.md).
- Steam lobby/session ownership: [Steam guide](../steam/AGENTS.md).
- Starter content only: read [starter guide](../starter/AGENTS.md), then the
  affected starter shell sources.
- Runtime execution: repository testing/operator protocols; investigation only
  after a terminal attempt and explicit authorization.

World owns PlayerLifecycle, NetworkWorld and player-first authoritative teardown.
Content uses that world but never resets identities, spawns players or owns sessions.
Empty RoundContentPath means no content; spawn-data provider is required.
Invalid configured paths fail clearly; no starter fallback or global type discovery.
Client content never spawns; clients observe server despawns. Reset local content
state after world-clear; terminal Stop must preserve objects until peer teardown.
Never clear live NetworkWorld/Netfox identities or replace transport during reset.
Do not automatically load GAS/carry/interaction/vendor/history/release docs.
