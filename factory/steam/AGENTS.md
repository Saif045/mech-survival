# Steam scope

Read [README.md](README.md) for platform/session, lobby, adapter and native peer
work. For actual connection diagnosis add relevant sections of
[integration notes](../docs/steam-integration.md) and the testing/investigation
protocols; do not load host/VM histories for routine lobby changes.

- SteamPlatform owns process initialization; SteamSession owns lobby/peer lifetime.
- Steam/account IDs and Godot PeerId differ; use adapter mapping APIs.
- Steam transport is below MultiplayerApi; diagnose lobby → native peer →
  Godot connection before blaming lifecycle or Netfox.
- Preserve observable peer creation/assignment and idempotent teardown.
- Dedicated hosting, authentication and host migration are unsupported.
