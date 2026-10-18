# Mech Survival

A co-op mech game consuming GameFactory mechanisms for authoritative gameplay, Steam sessions
and Netfox movement. Reusable mechanisms and the working reference experience
are supplied under [factory/](factory/README.md).

- [Current state](factory/docs/current-state.md)
- [Ownership and placement](factory/docs/ownership.md)
- [Consuming GameFactory](factory/docs/consuming-gamefactory.md)
- [v0.2 ownership migration](factory/docs/v0.2-ownership-migration.md)
- [Agent routing](AGENTS.md)

Mech Survival owns product code under `game/`; `addons/` and `third_party/` contain vendor
material. Root project, solution and export settings are shared integration.
The starter is a complete example, not a required player or gameplay model.

Build with `dotnet build GameFactory.sln`. Run both `dotnet test game/tests/MechSurvival.Tests/MechSurvival.Tests.csproj` and `dotnet test factory/tests/GameFactory.Tests/GameFactory.Tests.csproj`.
Local validation tools live in `factory/tools/`; focused runtime probes live in
`factory/sandbox/` and preserve evidence under `factory/artifacts/`.

Product contracts and validation: [game guide](game/AGENTS.md), [v0.2 migration](game/docs/v0.2-consumer-migration.md). Game-owned runners live in game/probes/ and preserve evidence in game/artifacts/.
