using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell.RoundComposition;

/// <summary>Custom/empty real host-client round reuse with the production lifecycle.</summary>
public partial class RoundCompositionProbe : Node
{
    private ENetMultiplayerPeer _peer = null!;
    private OnlineGameplayWorld _gameplay = null!;
    private NetworkWorld _world = null!;
    private RoundFixtureContent _content = null!;
    private RoundFixtureSpawnData _spawn = null!;
    private bool _host, _empty, _done;
    private long _clientPeer, _previousMax;
    private int _readyRound, _clearedRound;
    private ulong _deadline;
    public override void _Ready() => CallDeferred(MethodName.Run);
    private async void Run()
    {
        try
        {
            _host = OS.GetCmdlineUserArgs().Contains("--composition-host");
            _empty = OS.GetCmdlineUserArgs().Contains("--empty-content");
            _peer = new ENetMultiplayerPeer();
            Require((_host ? _peer.CreateServer(24881, 1) : _peer.CreateClient("127.0.0.1", 24881)) == Error.Ok, "ENet creation");
            Multiplayer.MultiplayerPeer = _peer;
            _gameplay = GD.Load<PackedScene>("res://factory/sandbox/shell/round_composition/gameplay.tscn").Instantiate<OnlineGameplayWorld>();
            if (_empty) _gameplay.RoundContentPath = new NodePath("");
            AddChild(_gameplay);
            _world = _gameplay.GetNode<NetworkWorld>("NetworkWorld");
            _content = _gameplay.GetNode<RoundFixtureContent>("GameContent");
            _spawn = _gameplay.GetNode<RoundFixtureSpawnData>("SpawnRules");
            _deadline = Time.GetTicksMsec() + 30000;
            if (!_host) { Multiplayer.ServerDisconnected += OnDisconnected; return; }
            CheckConfiguration();
            GD.Print("COMPOSITION_HOST_READY");
            await Until(() => Multiplayer.GetPeers().Length == 1, "Client connection");
            _clientPeer = Multiplayer.GetPeers().Single();
            for (int round = 1; round <= 2; round++)
            {
                Require(_gameplay.IsEmpty, "Round not empty before start");
                _gameplay.Start();
                CheckRound(round);
                RpcId(_clientPeer, MethodName.RoundReadyRpc, round);
                await Until(() => _readyRound == round, "Client custom-round observation");
                PlayerRegistry players = Field<PlayerRegistry>("_players");
                RoundFixturePlayer[] playerNodes = _world.Objects.Select(obj => obj.Host).OfType<RoundFixturePlayer>().ToArray();
                Node[] objects = _world.Objects.Select(obj => obj.Host).OfType<RoundFixtureObject>().Cast<Node>().ToArray();
                int exits = 0;
                bool playersFirst = true;
                foreach (RoundFixturePlayer player in playerNodes)
                    player.OnExit = () => { exits++; playersFirst &= objects.All(node => GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion()); };
                RpcId(_clientPeer, MethodName.ResetRpc, round);
                await _gameplay.ResetRoundAsync();
                Require(exits == 2 && playersFirst && players.Count == 0 && Field<PeerRegistry>("_peers").Count == 0,
                    "Player-first teardown/registry cleanup");
                Require(playerNodes.All(node => !GodotObject.IsInstanceValid(node)) && objects.All(node => !GodotObject.IsInstanceValid(node)), "Old hosts survived");
                CheckReset(round);
                await Until(() => _clearedRound == round, "Client clear acknowledgement");
                GD.Print($"COMPOSITION_PASS round={round} empty={_empty} players_first=true max_id={_previousMax}");
            }
            if (!_empty)
            {
                // Terminal Stop clears provider subscriptions while preserving nodes for peer teardown.
                _gameplay.Start();
                Require(_content.Subscribed, "Terminal content setup");
                int count = _world.Count;
                _gameplay.Stop();
                _gameplay.Stop();
                Require(!_content.Subscribed && _content.LocalRoundValue == 0 && _content.Resets == 3 && _world.Count == count,
                    "Stop cleanup must run once and preserve replicated nodes");
                GD.Print("COMPOSITION_PASS assertion=terminal_stop_local_cleanup");
            }
            RpcId(_clientPeer, MethodName.FinishRpc);
        }
        catch (Exception error) { Fail(error); }
    }
    private void CheckRound(int round)
    {
        Require(_world.Count == (_empty ? 2 : 4), "Wrong round topology/starter content leak");
        Require(_world.Objects.All(obj => obj.Id.Value > _previousMax), "World object IDs reused");
        _previousMax = _world.Objects.Max(obj => obj.Id.Value);
        Require(_world.Objects.All(obj => obj.Host is RoundFixturePlayer or RoundFixtureObject), "Starter object leaked");
        RoundFixturePlayer[] players = _world.Objects.Select(obj => obj.Host).OfType<RoundFixturePlayer>().ToArray();
        Require(players.Length == 2, "Two custom players expected");
        foreach (RoundFixturePlayer player in players)
        {
            NetworkObject obj = player.GetNode<NetworkObject>("NetworkObject");
            bool local = obj.OwnerPeerId == PeerId.Server;
            Require(player.InitializedBeforeTree && player.PlayerNumber == (local ? 1 : 2) &&
                player.SpawnPosition == new Vector3(local ? -6 : 6, 3, 4) && player.Position == player.SpawnPosition &&
                player.Loadout == (local ? "scout" : "engineer") && player.GetMultiplayerAuthority() == 1,
                "Custom initialization/authority mismatch");
            if (_host)
            {
                NetworkPlayer? record = Field<PlayerRegistry>("_players").FindByPeer(obj.OwnerPeerId);
                Require(record is not null && record.Id.Value == player.PlayerNumber && record.ObjectId == obj.Id &&
                    record.PeerId == obj.OwnerPeerId, "Registry identities do not match spawn data");
            }
        }
        RoundFixtureObject[] content = _world.Objects.Select(obj => obj.Host).OfType<RoundFixtureObject>().ToArray();
        Require(content.Length == (_empty ? 0 : 2), "Content count");
        if (!_empty) Require(content.Select(node => node.Kind).Order().SequenceEqual(new[] { "beacon", "crate" }) &&
            content.All(node => node.InitializedBeforeTree && node.RoundValue == round && node.GetMultiplayerAuthority() == 1), "Fresh content initialization");
        Require(_spawn.Calls == (_host ? round * 2 : 0), "Spawn data was not server-only");
        Require(_content.Starts == (_host && !_empty ? round : 0), "Content was not server-only/explicit");
    }
    private void CheckReset(int round)
    {
        Require(_gameplay.IsEmpty && _world.Count == 0 && ReferenceEquals(Multiplayer.MultiplayerPeer, _peer), "Empty world/retained peer");
        Require(!_content.Subscribed && _content.LocalRoundValue == 0 && _content.Resets == (_empty ? 0 : round) &&
            _content.LastResetWorldCount == 0, "Content reset did not clear local state after world-clear");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void RoundReadyRpc(int round)
    {
        try { _gameplay.Start(); await Until(() => _world.Count == (_empty ? 2 : 4), "Client spawn replicas"); CheckRound(round); GD.Print($"COMPOSITION_CLIENT_OBSERVED round={round} empty={_empty}"); RpcId(1, MethodName.ReadyRpc, round); }
        catch (Exception error) { Fail(error); }
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReadyRpc(int round)
    {
        if (Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == _clientPeer) _readyRound = round;
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void ResetRpc(int round)
    {
        try { await _gameplay.ResetRoundAsync(); CheckReset(round); RpcId(1, MethodName.ClearedRpc, round); }
        catch (Exception error) { Fail(error); }
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClearedRpc(int round)
    {
        if (Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == _clientPeer) _clearedRound = round;
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishRpc() { _done = true; GD.Print("ROUND_COMPOSITION_PASS role=client"); RpcId(1, MethodName.FinishedRpc); }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishedRpc()
    {
        if (Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == _clientPeer) { _done = true; GD.Print("ROUND_COMPOSITION_PASS role=host"); GetTree().Quit(); }
    }
    private static void CheckConfiguration()
    {
        OnlineGameplayWorld world = new();
        world.AddChild(new RoundFixtureSpawnData { Name = "Provider" });
        world.AddChild(new Node { Name = "Wrong" });
        MethodInfo resolve = typeof(OnlineGameplayWorld).GetMethod("ResolveComposition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            foreach ((string spawn, string content) in new[] { ("", ""), ("Missing", ""), ("Wrong", ""), ("Provider", "Missing"), ("Provider", "Wrong") })
            {
                world.SpawnDataProviderPath = new NodePath(spawn); world.RoundContentPath = new NodePath(content);
                bool rejected = false;
                try { resolve.Invoke(world, null); } catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { rejected = true; }
                Require(rejected, "Invalid configured composition did not fail closed");
            }
            world.SpawnDataProviderPath = new NodePath("Provider"); world.RoundContentPath = new NodePath("");
            resolve.Invoke(world, null);
        }
        finally { world.Free(); }
        GD.Print("COMPOSITION_PASS assertion=configuration_and_explicit_empty");
    }
    private T Field<T>(string name) => (T)typeof(OnlineGameplayWorld).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_gameplay)!;
    private async Task Until(Func<bool> condition, string label)
    {
        ulong deadline = Time.GetTicksMsec() + 5000;
        while (!condition()) { Require(Time.GetTicksMsec() < deadline, label + " timed out"); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
    public override void _Process(double delta) { if (_deadline > 0 && Time.GetTicksMsec() >= _deadline) Fail(new TimeoutException("Round composition timeout")); }
    private void OnDisconnected() { if (_done) GetTree().Quit(); else Fail(new InvalidOperationException("Premature host disconnect")); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.Print("ROUND_COMPOSITION_FAIL: " + error); _done = true; GetTree().Quit(1); }
    public override void _ExitTree()
    {
        if (!_host) Multiplayer.ServerDisconnected -= OnDisconnected;
        _gameplay?.Stop();
        if (_peer is not null) { _peer.Close(); Multiplayer.MultiplayerPeer = null; _peer.Dispose(); }
    }
}
