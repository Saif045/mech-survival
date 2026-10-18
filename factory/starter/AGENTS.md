# Starter scope

Read only the relevant [README.md](README.md) map/section, then affected sources.
Player composition: [player guide](player/README.md). GAS changes:
[GAS scope](gameplay/gas/AGENTS.md). Inventory-only work needs the map's inventory
section and affected inventory/item sources, not all GAS/carry documentation.
For reusable mechanism changes use the corresponding factory gameplay/shell scope.

Starter may use reusable mechanisms. Reusable code must not use starter or
game types/resources. Inventory, vitals, equipment effects, revive and sample
objects are reference semantics, never required game models. Keep presentation
outside rollback, GAS canonical on the server, and server spawning/identity in
NetworkWorld/PlayerLifecycle. Do not load all starter or historical probe docs.
