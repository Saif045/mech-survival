# Physical ownership and placement

This is the canonical placement guide, established by
[ADR 0008](decisions/0008-runtime-starter-consumer-ownership.md).

| Ownership | Paths | Rule |
| --- | --- | --- |
| GameFactory | `factory/**` | Upstream maintains mechanisms, starter and development material. Reusable changes return upstream before official downstream release integration. |
| Product | `game/**` | Games own mechanics, levels, items, UI, progression, assets, tests and product docs. Create only directories the game needs. |
| Vendor | `addons/**`, `third_party/**` | Third-party dependencies, licenses, external material and explicit vendor patches. Preserve their provenance. |
| Shared integration | Root project, solution/project files, export/configuration, icon | Manually reconcile factory, game and dependency requirements. |
| Repository entry points | Root README and AGENTS | Concise landing page and ownership routing; downstream may add product routing. |

## Inside factory

Reusable mechanisms live in `runtime/`, `diagnostics/`, `networking/`, `steam/`,
`gameplay/` and `shell/`. They supply lifecycle, identity, authority and focused
composition contracts. They must not depend on `factory/starter/**` or `game/**`
types, resources or implicit gameplay defaults.

`starter/` is the working reference/default experience. It depends on mechanisms
and owns its player, GAS/vitals, inventory/equipment, revive, switch/cube and
round/spawn policy. It is upstream-maintained reference material; a game's
different semantics belong in `game/`, not a permanent fork of starter.
Games can compose normal Godot nodes and mechanism interfaces directly.

`sandbox/` contains experiments, launchers and runtime fixtures. `tests/` holds
automated verification, including the separate xUnit project. `tools/` holds
validation, export and host/VM helpers. `artifacts/` holds ignored run evidence,
including relocated immutable historical exports and logs. Do not rewrite
captured history to make its embedded old paths look current. These directories
are development material, not product gameplay or reusable runtime APIs.

Root `project.godot`, `GameFactory.csproj`, `GameFactory.sln`, export presets,
Git/editor configuration and `icon.svg` operate at project/repository scope.
`.godot/`, `build/`, disposable `.tmp-*` and editor/template configuration are
generated/local integration state, not first-party source ownership zones.
No empty speculative `game/` or new packaging directories are needed upstream.

GodotGAS resources have no required root location: the starter uses explicit
project settings and stores them under `starter/gameplay/gas/resources/`.
Vendor default strings in the unmodified addon are legitimate dependency
defaults, not stale first-party references.

## Documentation and task context

One subsystem has one canonical documentation home: its README explains the
contract, and its compact AGENTS guide routes tasks and records critical
invariants. Cross-cutting knowledge lives here in `factory/docs/`; do not create
a second centralized mechanism documentation tree. Follow root AGENTS →
factory AGENTS → nearest scope → README when detail is needed → targeted code.
Extensive repository knowledge does not require extensive per-task context.
Do not recursively read unrelated guides, all starter documentation or historical
acceptance records unless the task crosses those boundaries.

Use [consumer guidance](consuming-gamefactory.md) for release integration and
[migration notes](v0.2-ownership-migration.md) for the complete move/API map.
