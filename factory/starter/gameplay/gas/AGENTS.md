# Starter GAS scope

Read [README.md](README.md) for starter ownership and the server/projection
composition pattern, then affected adapter/bridge/ability resources only.
Use vendored GodotGAS docs/source for raw addon API questions.

- NetworkGasComponent is starter-specific; do not attach it to custom actors
  simply to use GAS. Games author their own semantics/adapters/projections.
- Canonical GAS stays on server; client mirrors are hints/read-only consumers.
- Remove effects by their source and preserve unrelated effects during refresh.
- Keep raw C#↔GDScript calls inside the adapter; pair subscriptions and cleanup.
- GAS never enters rollback. Read Netfox guidance only for actual physical
  projection/replay boundary changes; do not modify vendor code incidentally.
