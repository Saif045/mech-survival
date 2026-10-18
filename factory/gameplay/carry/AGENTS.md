# Carry scope

For carry implementation, read [README.md](README.md), the canonical carry
contract/composition/migration guide. Do not recursively load unrelated docs.
For pickup request changes, also read [interaction guidance](../interaction/AGENTS.md).
Read inventory source/guidance only when a task crosses starter inventory
compatibility. Read [Netfox guidance](../../networking/netfox/AGENTS.md) only
if a proposed change affects rollback; carry normally must not do so.

- Item root/state authority stays on server 1; represented ownership is metadata.
- Holder ID is NetworkObjectId, never PeerId or durable/account identity.
- Actor permission and anchor are explicit; invalid configuration fails closed.
- Pickup uses the existing interaction path; drop accepts no client transform.
- Holder and target capabilities resolve on host or direct children, not recursively.
- Local held presentation is derived and outside rollback.
- Neutral lifecycle release bypasses input permission, still checks holder identity.
- Stored/Equipped/GAS/downed semantics belong to starter/game code.
- Runtime tests follow the repository protocols; do not commit before acceptance.
