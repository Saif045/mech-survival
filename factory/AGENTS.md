# GameFactory task router

Scope: all first-party foundation, starter and development material in `factory/`.
Read only the nearest relevant scope below and its canonical README when needed,
then targeted source. Durable knowledge may be extensive; per-task context stays
small. Do not recursively load unrelated guides, chat history or acceptance logs.

- Reusable mechanisms must not depend on starter/game types or resources.
  Games own product semantics; starter is optional reference composition.
- Shared state is server-authoritative. Keep account, PeerId, PlayerId,
  NetworkObjectId and represented-owner metadata distinct. Clients never
  authoritatively spawn players; OwnerPeerId does not grant root authority.
- Compose normal Godot nodes and existing Netfox primitives. Do not build a
  second engine, command bus, transport abstraction or rollback framework.
- Preserve unrelated changes; stage focused task files. Never claim an unrun
  validation. Required runtime acceptance must pass before implementation commit.

| Task | Minimum next scope/document |
| --- | --- |
| Discover mechanisms | [catalog](README.md) |
| Identity, lifecycle, authority, replication, world spawning | [networking](networking/AGENTS.md) |
| Rollback/custom player | [Netfox](networking/netfox/AGENTS.md) |
| Interaction | [interaction](gameplay/interaction/AGENTS.md) |
| Carry | [carry](gameplay/carry/AGENTS.md) |
| Other reusable gameplay placement | [gameplay](gameplay/AGENTS.md) |
| Round/session composition | [shell](shell/AGENTS.md) |
| Steam lobby, adapter, native peer | [Steam](steam/AGENTS.md) |
| Runtime role policy | [runtime](runtime/AGENTS.md) |
| Logging/distributed evidence | [diagnostics](diagnostics/AGENTS.md) |
| Starter-only player/gameplay/inventory | [starter](starter/AGENTS.md) |
| Ownership or downstream integration | [ownership](docs/ownership.md), [consumer workflow](docs/consuming-gamefactory.md) |
| v0.2 path/API migration | Relevant section of [migration](docs/v0.2-ownership-migration.md) |
| Cross-module architecture change | [architecture](docs/architecture.md), relevant ADR only |
| Maturity/blockers/acceptance claims | Relevant section of [current state](docs/current-state.md) |
| Validation planning | [validation](docs/testing.md) |
| Godot/Steam/process execution | [testing protocol](docs/testing-protocol.md) |
| Host/VM execution | [A/B scope](tools/ab_test/AGENTS.md) |
| Authorized investigation after terminal attempt | [investigation protocol](docs/investigation-protocol.md) |

Search before opening large files/logs. Add only failing evidence for bugs and
current run evidence for validation. Runtime tests need assertions, deadlines,
preflight, frozen inputs, terminal PASS/FAIL/BLOCKED and verified cleanup; a
window opening is not acceptance. Diagnose transport before lifecycle, Netfox
or gameplay. At completion report changes, actual validation and commit SHA.
