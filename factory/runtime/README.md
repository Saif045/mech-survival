# Runtime roles

`GameFactory.Runtime.RuntimeContext` describes the process role for lifecycle
policy. It does not initialize a transport or prove that a peer is connected.

| RuntimeMode | IsServer | IsClient | Meaning |
| --- | --- | --- | --- |
| Offline | false | false | No online role; IsOffline is true |
| ListenServer | true | true | Authoritative server with a local player |
| Client | false | true | Remote authority owns shared state |
| DedicatedServer | true | false | Server role without a local player |

Construct a context for pure policy/test composition, for example
`new RuntimeContext(RuntimeMode.ListenServer)`. Mode changes/reset are internal;
normal online composition is owned by OnlineGameplayWorld, which derives the
role at start and resets at teardown. Do not expose client requests that switch
authority by changing mode. DedicatedServer is a policy role, not an implemented
Steam dedicated-host API.

PlayerLifecycle uses this context to suppress client spawning and exclude a
dedicated server's local peer. The smallest custom online composition uses
[shell/world](../shell/README.md), which owns context and registries; a consumer
does not need a RuntimeContext scene node or starter inheritance. For custom
low-level lifecycle use [networking](../networking/README.md#player-lifecycle).
There is no RPC, replication or rollback state in RuntimeContext. Games own
game-mode meaning; the factory owns these role distinctions.

Validation: [PlayerLifecycleTests](../tests/GameFactory.Tests/PlayerLifecycleTests.cs)
covers role policy; [custom lifecycle runner](../sandbox/shell/run_custom_player_lifecycle_probe.ps1)
covers composition. Current evidence → [current state](../docs/current-state.md).
