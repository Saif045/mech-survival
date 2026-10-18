# Networking scope

Read [README.md](README.md) for identity, lifecycle, NetworkObject, authority,
replication or spawning. For rollback-only work use [Netfox](netfox/AGENTS.md)
directly; add this guide only when changing the shared networking boundary.
Steam transport work routes to [Steam](../steam/AGENTS.md).

- NetworkWorld server-allocates IDs and owns MultiplayerSpawner routing.
- OwnerPeerId is represented-owner metadata, never automatic root authority.
- Resolve runtime targets by NetworkObjectId in their world, not client NodePaths.
- PeerId is a live connection, not durable player/account identity.
- Bind objects and apply spawn data before tree entry. Configure callbacks run
  only on the server; replica initialization belongs in spawn data.
- Ordinary replication is state convergence, not delivery of every action.
- Add architecture/ADRs only for a cross-module ownership change.
