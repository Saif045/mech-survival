using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Runtime;

namespace GameFactory.Shell;

/// <summary>
/// The small, visible gameplay composition used by the online shell. Steam
/// session ownership stays with <see cref="OnlineGameplayShell"/>; this node
/// only creates and removes the server-authoritative gameplay world.
/// </summary>
public partial class OnlineGameplayWorld : Node3D
{
    private readonly PeerRegistry _peers = new();
    private readonly PlayerRegistry _players = new();
    private readonly RuntimeContext _runtime = new();
    private NetworkWorld _world = null!;
    private PlayerLifecycle? _playerLifecycle;
    private bool _started;
    private IRoundContent? _roundContent;
    private IPlayerSpawnDataProvider? _spawnDataProvider;
    private bool _contentActive;
    private bool _configured;
    private bool _subscribed;

    public bool IsEmpty => !_started && !_contentActive && _playerLifecycle is null &&
        _players.Count == 0 && _peers.Count == 0 && _world.Count == 0;

    public Dictionary<string, string?> StateFields() => new()
    {
        ["players"] = _players.Count.ToString(),
        ["peers"] = _peers.Count.ToString(),
        ["network_objects"] = _world.Count.ToString(),
        ["player_lifecycle_active"] = (_playerLifecycle is not null).ToString(),
        ["round_content_active"] = _contentActive.ToString()
    };

    [Export] public PackedScene PlayerScene { get; set; } = null!;
    /// <summary>Relative to this world. Empty explicitly means no dynamic round content.</summary>
    [Export] public NodePath RoundContentPath { get; set; } = new("");
    /// <summary>Required explicitly composed initialization-data provider.</summary>
    [Export] public NodePath SpawnDataProviderPath { get; set; } = new("");

    public override void _Ready()
    {
        _world = GetNode<NetworkWorld>("NetworkWorld");
        ResolveComposition();
        _configured = true;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        _subscribed = true;
    }

    private void ResolveComposition()
    {
        if (SpawnDataProviderPath.IsEmpty || GetNodeOrNull<Node>(SpawnDataProviderPath) is not IPlayerSpawnDataProvider spawnData)
            throw new InvalidOperationException("OnlineGameplayWorld requires SpawnDataProviderPath to reference an IPlayerSpawnDataProvider node.");
        _spawnDataProvider = spawnData;
        if (RoundContentPath.IsEmpty) { _roundContent = null; return; }
        _roundContent = GetNodeOrNull<Node>(RoundContentPath) as IRoundContent
            ?? throw new InvalidOperationException("OnlineGameplayWorld RoundContentPath must reference an IRoundContent node or be empty.");
    }

    public void Start()
    {
        if (_started) return;
        if (!_configured) throw new InvalidOperationException("OnlineGameplayWorld composition is not valid/ready.");
        if (Multiplayer.IsServer() && !IsEmpty)
            throw new InvalidOperationException("The previous gameplay world is not empty.");
        _started = true;
        _contentActive = _roundContent is not null;

        if (!Multiplayer.IsServer())
        {
            _runtime.SetMode(RuntimeMode.Client);
            GameLog.Info("shell.gameplay", "client_started");
            return;
        }

        _runtime.SetMode(RuntimeMode.ListenServer);
        _peers.Add(PeerId.Server, isLocal: true);
        foreach (int peerValue in Multiplayer.GetPeers())
        {
            // SteamMultiplayerPeer may include the listen server (1) in this
            // list. The server was registered above as local, not remote.
            if (peerValue > PeerId.Server.Value)
                _peers.Add(new PeerId(peerValue), isLocal: false);
        }

        _playerLifecycle = new PlayerLifecycle(_peers, _players, _runtime, SpawnPlayer, _world.Despawn);
        _roundContent?.StartRound(_world);
        GameLog.Info("shell.gameplay", "host_started", fields: new Dictionary<string, string?>
        {
            ["players"] = _players.Count.ToString(),
            ["network_objects"] = _world.Count.ToString()
        });
    }

    public void Stop()
    {
        if (!_started && _playerLifecycle is null && !_contentActive) return;
        _started = false;
        _playerLifecycle?.Dispose();
        _playerLifecycle = null;
        _peers.Clear();
        ResetContent();
        GameLog.Info("shell.gameplay", "stopped");
    }

    /// <summary>Reset only round objects; the shell keeps its connected Steam session.</summary>
    public async Task ResetRoundAsync()
    {
        _started = false;
        GameLog.Info("shell.gameplay", "round_teardown_started", fields: StateFields());
        if (Multiplayer.IsServer())
        {
            // Peer removal must reach the live lifecycle before it is disposed.
            // Queued player exits release hidden items and remove GAS effects.
            NetworkObjectId[] playerObjectIds = _players.Players.Select(player => player.ObjectId).ToArray();
            _peers.Clear();
            await WaitUntilAsync(() => playerObjectIds.All(id => !_world.TryGet(id, out _)));
            foreach (NetworkObject obj in _world.Objects.ToArray())
                _world.Despawn(obj.Id);
        }
        // Clients observe authoritative MultiplayerSpawner despawns; they never
        // free replicated objects themselves or reset the world's ID allocator.
        await WaitUntilAsync(() => _world.Count == 0);
        _playerLifecycle?.Dispose();
        _playerLifecycle = null;
        ResetContent();
        if (!IsEmpty) throw new InvalidOperationException("Round teardown left gameplay state registered.");
        GameLog.Info("shell.gameplay", "round_world_cleared", fields: StateFields());
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        ulong deadline = Time.GetTicksMsec() + 10000;
        while (!condition())
        {
            if (!IsInsideTree() || Time.GetTicksMsec() >= deadline)
                throw new TimeoutException("Round world did not clear within 10 seconds.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    public override void _ExitTree()
    {
        if (_subscribed) Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Stop();
    }

    private void ResetContent()
    {
        if (!_contentActive) return;
        _roundContent!.ResetRound();
        _contentActive = false;
    }

    private NetworkObjectId SpawnPlayer(NetworkPeer peer, PlayerId playerId)
    {
        Godot.Collections.Dictionary data = _spawnDataProvider!.CreateSpawnData(peer, playerId)
            ?? throw new InvalidOperationException("Player spawn-data provider returned null.");
        Node player = _world.Spawn<Node>(PlayerScene, peer.Id, data);
        return NetworkObject.RequireFromHost(player).Id;
    }

    private void OnPeerDisconnected(long peerValue)
    {
        if (_started && Multiplayer.IsServer())
        {
            _peers.Remove(new PeerId(peerValue));
            GameLog.Info("shell.gameplay", "gameplay_peer_left", fields: StateFields());
        }
    }
}
