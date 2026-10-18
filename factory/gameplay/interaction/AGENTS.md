# Interaction scope

Read [README.md](README.md) for interaction contract, composition and validation.
For pickup-specific work add [carry](../carry/AGENTS.md); for starter revive or
GAS permission change use [starter](../../starter/AGENTS.md).

- Client supplies only target NetworkObjectId; server resolves the same world
  and validates sender/represented owner, actor permission, self-target, range
  and target CanInteract.
- Explicit PermissionPath is required and invalid configuration fails closed.
- Local policy/CanInteract are side-effect-free hints; server dispatch decides.
- Actor permission is independent of target rules, GAS and player classes.
