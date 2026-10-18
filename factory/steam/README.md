# Steam platform, session and transport

Use this subsystem for the implemented Steam listen-server path. It is a
Steam-specific boundary below Godot MultiplayerApi, not a cross-platform online
framework. Gameplay RPCs, spawners, synchronizers and Netfox remain above it.

## Ownership and APIs

| Type | Lifetime / responsibility |
| --- | --- |
| SteamPlatform (autoload Node) | Process lifetime; creates GodotSteamAdapter, initializes once and shuts down on application exit |
| SteamSession (IDisposable) | Scene/session lifetime; one lobby and installed MultiplayerPeer; state/errors/invite events |
| ISteamAdapter | Typed Steam initialization, friends/presence, lobby, overlay, ID mapping and peer creation API |
| GodotSteamAdapter | C# facade over the project GDScript bridge |
| godot_steam_bridge.gd | Only factory layer directly calling GodotSteam and SteamMultiplayerPeer |

Types live in `GameFactory.Steam`, adapter in
`GameFactory.Steam.Adapters.GodotSteam`, and data values in `.Models`.
Instance [steam_platform.tscn](steam_platform.tscn) as the shared SteamPlatform
autoload. It exposes `ReadyTask` and `Adapter`; GodotSteam auto-initialization
is disabled in project settings. Do not initialize or shut down Steam per player
or round. The platform owns the adapter; scene-local consumers do not dispose it.

## Minimal session composition

Prefer [OnlineGameplayShell](../shell/README.md) for a playable custom game;
it composes SteamSession, lobby readiness and reusable rounds already. A focused
scene owning just a session uses the existing public APIs:

```csharp
// In an async scene-owned method; store session for later cleanup:
var platform = GetNode<SteamPlatform>("/root/SteamPlatform");
await platform.ReadyTask;
session = new SteamSession(platform.Adapter, Multiplayer);
await session.InitializeAsync();
await session.HostAsync(new SteamLobbyCreateOptions(), new SteamListenServerOptions());
// Client alternative: await session.JoinAsync(lobbyId, new SteamClientOptions());
// On explicit leave: await session.LeaveAsync();
// On scene disposal: session.Dispose();
```

Use `GameFactory.Steam` and `GameFactory.Steam.Models` imports. `session` is a
scene-owned SteamSession field and lobbyId a SteamLobbyId. The options models
provide defaults; games choose supported lobby/member/data policy. Subscribe to
StateChanged/LobbyJoinRequested only for the lifetime of your owner and remove
subscriptions at teardown. Hosting/Connected session states record peer
installation; they are not substitutes for native Connected and Godot
ConnectedToServer evidence.

Host flow is initialize → create lobby → create listen-server Steam peer →
assign to MultiplayerApi. Client flow is join lobby → create lobby client peer
→ assign → native handshake → Godot connection signal. The lobby owner is a
Steam account, not automatically a gameplay authority-transfer mechanism.
Listen server is Godot peer 1. Use `TryGetSteamUserForPeer` and
`TryGetPeerForSteamUser` on the adapter for optional mapping; never cast SteamUserId
to PeerId or store transient PeerId as an account identity.

## Leave, reuse and game policy

Leave closes/disposes the native peer, clears Godot's peer, then leaves the lobby;
successful leave returns the session to Ready. Teardown is idempotent for
already-disposed peers and still attempts lobby leave after a peer error.
Dispose tears down and unsubscribes; await LeaveAsync for an explicit completed
leave before discarding the owner. `PeerTearingDown` allows best-effort diagnostic
flush before removal. The shared process platform remains initialized for another
session, including a later opposite host/client role.

Round reuse retains lobby/peer and returns Gameplay → Lobby through shell/world
reset, not LeaveAsync. Shell owns readiness/revisions, joinability and phase
policy; games own round content. Lobby joining/rejoining is supported;
gameplay-phase joining is intentionally rejected. Invites are surfaced and
active-session replacement is rejected by normal shell routing.

Overlay attachment is not networking-health proof. The application provides
Join Game fallback when Invite Friends cannot open the overlay. Development
uses App ID 480; production App ID/launch normalization, cold-start invites,
dedicated Steam hosting, authentication, host migration and shipping readiness
are unsupported/deferred. Lobby metadata and platform IDs do not authorize
gameplay mutations. No Steam state belongs in rollback history.

## Examples and validation

Reference: [starter shell scene](../starter/shell/online_gameplay.tscn).
Focused lobby probe: [steam_probe.tscn](../sandbox/steam/steam_probe.tscn);
native host/close/rehost smoke:
[steam_native_rehost_probe.tscn](../sandbox/steam/steam_native_rehost_probe.tscn).
Real two-account testing uses [A/B scope](../tools/ab_test/AGENTS.md) and its
protocols. Current limitations/evidence → [current state](../docs/current-state.md).
For connection failures, use targeted [integration/investigation notes](../docs/steam-integration.md)
and [vendor patch provenance](../../third_party/patches/godotsteam/README.md).
Do not restart Steam inside a live attempt or diagnose gameplay from lobby-only evidence.
