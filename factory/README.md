# GameFactory mechanism catalog

GameFactory provides server-authoritative small-session co-op mechanisms.
Compose them directly with game-owned nodes, or explore the optional working
[starter](starter/README.md). Starter player, inventory and GAS types are examples,
not required bases. This is one production assembly, not an independently
packaged addon.

| Mechanism | What it solves | Canonical guide | Ownership |
| --- | --- | --- | --- |
| Runtime roles | Distinguishes offline, client, listen server and server without a local player | [runtime](runtime/README.md) | Reusable |
| PeerId / PlayerId / NetworkObjectId | Separates connection, session player and live object identity | [networking](networking/README.md#identities) | Reusable |
| PlayerLifecycle | Creates/removes authoritative players as peers change | [networking](networking/README.md#player-lifecycle) | Reusable |
| NetworkObject | Binds a normal scene host to a live world and focused components | [networking](networking/README.md#scene-composition) | Reusable |
| Authority components | Reports Godot authority independently of represented ownership | [networking](networking/README.md#authority-and-replication) | Reusable |
| Replication | Projects ordinary authoritative properties using Godot synchronizers | [networking](networking/README.md#authority-and-replication) | Reusable |
| NetworkWorld | Server-owned dynamic spawn/despawn, lookup and initialization | [networking](networking/README.md#spawn-and-despawn) | Reusable |
| Netfox rollback-player glue | Establishes server simulation / owner input topology | [Netfox](networking/netfox/README.md) | Reusable |
| Interaction | Resolves and validates discrete target requests on the server | [interaction](gameplay/interaction/README.md) | Reusable |
| Carry | Attaches/releases one world object with explicit permission and anchor | [carry](gameplay/carry/README.md) | Reusable |
| Shell / reusable rounds | Coordinates session reuse, world lifecycle and game-owned content | [shell](shell/README.md) | Reusable |
| Steam session/platform | Owns process initialization, lobby and native peer lifetime | [Steam](steam/README.md) | Reusable |
| Diagnostics | Writes local evidence and a bounded host-collected session timeline | [diagnostics](diagnostics/README.md) | Reusable |
| Player and reference gameplay | Demonstrates input/simulation/presentation, inventory, revive and sample items | [starter map](starter/README.md), [player](starter/player/README.md) | Starter/reference |
| GAS composition pattern | Demonstrates canonical server GAS and explicit client/physics projections | [starter GAS](starter/gameplay/gas/README.md) | Starter/reference; games own their GAS model |

For a normal task follow [AGENTS.md](AGENTS.md) to the nearest scope, its guide
when needed, then targeted source. Use [consumer workflow](docs/consuming-gamefactory.md)
for release integration, [ownership](docs/ownership.md) for placement, and
[current state](docs/current-state.md) only for maturity, blockers or evidence.
Cross-cutting architecture, protocols, ADRs and release records live in `docs/`;
`sandbox/`, `tests/`, `tools/` and `artifacts/` hold verification and development material.
