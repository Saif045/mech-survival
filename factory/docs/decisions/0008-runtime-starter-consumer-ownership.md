# ADR 0008: Runtime, Starter, and Consumer Ownership Boundary

- Status: Accepted
- Date: 2026-10-12
- Related: [ADR 0005](0005-one-assembly-until-boundaries-justify-packaging.md),
  [ADR 0007](0007-upstream-downstream-distribution.md), [ownership](../ownership.md)

## Context

The real downstream `Saif045/mech-survival` consumer exposed player/gameplay
coupling and a gameplay-routing defect. Tasks A–C established explicit actor
permission, carry capability and round content/spawn-data seams. First-party
material scattered across root directories and reference gameplay in reusable
namespaces still obscured those boundaries for downstream contributors.

## Decision

Group first-party GameFactory mechanisms, reference composition, probes, tests,
tools, cross-cutting docs and evidence physically under `factory/`. Put reference
gameplay in `factory/starter/` with clear starter namespaces. Reusable mechanisms
cannot depend on starter or `game/` gameplay; starter and games may depend on
mechanisms. Games own their semantics/content in `game/` and can compose the
public contracts without starter inheritance.

Keep `addons/` and `third_party/` as vendor zones. Project/solution/export files,
project settings, icon and repository entry points are explicit root integration
exceptions. GodotGAS resource locations are configurable, so starter registries
move into starter and all resource settings are explicit. Preserve resource UIDs
and immutable historical evidence.

The one-production-assembly decision remains valid. Consumer evidence now exists,
but it justifies composition seams and ownership clarity, not a plugin/package
split. Independent packaging and binary compatibility remain deferred until a
concrete isolation/distribution requirement warrants another decision.

## Alternatives and consequences

Keeping broad root ownership lists would retain ambiguity. Splitting assemblies
now would add packaging/build complexity without a demonstrated requirement.
Physical paths and namespaces communicate the chosen boundary; source/dependency
review must enforce it while the production assembly remains shared.

The migration requires downstream resource, namespace, script and shared project
updates, described in [migration notes](../v0.2-ownership-migration.md).
GameShell's fallback becomes composition data to avoid a runtime-to-starter
resource dependency; default gameplay behavior remains unchanged.

Subsystem READMEs are canonical detailed homes, AGENTS files route to the minimum
relevant context, and cross-cutting docs stay in `factory/docs/`. Agents do not
recursively load unrelated guides or historical acceptance evidence.

## Validation

Validate moves in phases, then build/unit-test, clean-import and exercise the
default player/GAS/Netfox composition, Tasks A–C, lifecycle/reuse and normal
gameplay routes. Check helper discovery, dependency direction, stale tracked
paths and Markdown links. Record final evidence in current state; previous
Steam/VM runs are not rerun evidence for this structural task.
