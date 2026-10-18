# Netfox scope

Read [README.md](README.md) for GameFactory rollback composition. Add
[starter player](../../starter/player/README.md) only for that reference player.
Raw addon API work uses vendored source/docs linked in the README.

- Netfox is pinned; do not blindly update it or introduce parallel reconciliation.
- Set root/server Simulation and represented-owner Input authority before
  Netfox children initialize. Keep property lists visible on the prefab.
- NetworkEvents alone owns NetworkTime start/stop. Do not call process_settings
  during normal initialization or clear live identities during round reuse.
- Keep history 64 and input broadcast false unless evidence justifies change.
- Synchronize rollback inputs/state or derive deterministically; resimulation
  is not automatically a correction. Queue edge inputs.
- GAS, inventory, carry, commands, camera and cosmetics stay outside history.
- Transport/lifecycle failures require targeted diagnostic protocols, not all
  historical records by default.
