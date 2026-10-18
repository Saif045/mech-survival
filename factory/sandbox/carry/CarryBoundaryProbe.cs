using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Carry;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;

namespace GameFactory.Sandbox.Carry;

/// <summary>Real host/client RPC, state replication and derived carry presentation.</summary>
public partial class CarryBoundaryProbe : Node
{
    private ENetMultiplayerPeer _peer = null!;
    private NetworkWorld _world = null!;
    private Node3D _host = null!, _client = null!, _item = null!, _spare = null!;
    private long _clientPeer;
    private long _hostId, _clientId, _itemId, _spareId;
    private bool _isHost, _starter, _finished;
    private int _step = -1;
    private ulong _deadline;
    private Vector3 _dropPosition;
    private readonly List<string> _rejects = new();
    public override void _Ready()
    {
        try
        {
            GameLog.EnsureInitialized();
            _isHost = OS.GetCmdlineUserArgs().Contains("--carry-host");
            _starter = OS.GetCmdlineUserArgs().Contains("--starter");
            _peer = new ENetMultiplayerPeer();
            Require((_isHost ? _peer.CreateServer(24880, 1) : _peer.CreateClient("127.0.0.1", 24880)) == Error.Ok, "ENet preflight");
            Multiplayer.MultiplayerPeer = _peer;
            StaticBody3D floor = new();
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(80, .2f, 80) }, Position = new Vector3(0, -.1f, 0) });
            AddChild(floor);
            _world = new NetworkWorld { Name = "NetworkWorld" };
            AddChild(_world);
            GameLog.EntryWritten += Observe;
            if (_isHost) { Multiplayer.PeerConnected += StartScenario; GD.Print("CARRY_HOST_READY"); }
            else Multiplayer.ServerDisconnected += OnDisconnected;
            _deadline = Time.GetTicksMsec() + 35000;
        }
        catch (Exception error) { Fail(error); }
    }
    private void Observe(LogEntry entry)
    {
        if ((entry.Category == "carry" || entry.Category == "interaction") && entry.Event == "rejected" && entry.Fields.TryGetValue("reason", out string? reason))
            _rejects.Add(entry.Category + ":" + reason);
    }
    private void StartScenario(long peer)
    {
        try
        {
            _clientPeer = peer;
            CheckConfiguration();
            string actorPath = _starter ? "res://factory/starter/player/network_player_3d.tscn" : "res://factory/sandbox/carry/custom_carry_actor.tscn";
            PackedScene actor = GD.Load<PackedScene>(actorPath);
            _host = _world.Spawn<Node3D>(actor, PeerId.Server, new Godot.Collections.Dictionary { ["spawn_position"] = new Vector3(-.5f, 1, 0) });
            _client = _world.Spawn<Node3D>(actor, new PeerId(peer), new Godot.Collections.Dictionary { ["spawn_position"] = new Vector3(.5f, 1, 0) });
            PackedScene item = GD.Load<PackedScene>(_starter ? "res://factory/starter/gameplay/carry/carryable_item.tscn" : "res://factory/sandbox/carry/custom_carry_item.tscn");
            Variant spawn = _starter ? (Variant)new Godot.Collections.Dictionary { ["spawn_transform"] = new Transform3D(Basis.Identity, new Vector3(0, 1, 1)) } : default(Variant);
            _item = _world.Spawn<Node3D>(item, PeerId.Server, spawn);
            _spare = _world.Spawn<Node3D>(item, PeerId.Server, spawn);
            _hostId = Id(_host); _clientId = Id(_client); _itemId = Id(_item); _spareId = Id(_spare);
            Use(_host).RequestInteraction(new NetworkObjectId(_itemId));
            Require(Target(_item).HolderNetworkObjectId == _hostId, "Host pickup via interaction");
            Require(!Carrier(_host).TryPickup(Target(_spare)), "One-item capacity");
            Require(!Target(_item).TryRelease(_client.GetNode<NetworkObject>("NetworkObject"), Transform3D.Identity), "Non-holder target release");
            GD.Print("CARRY_PASS assertion=host_pickup_capacity_nonholder");
            Next(0);
        }
        catch (Exception error) { Fail(error); }
    }
    private void Next(int step)
    {
        _step = step; _rejects.Clear();
        RpcId(_clientPeer, MethodName.StepRpc, step, _hostId, _clientId, _itemId, _spareId, _dropPosition);
    }
    private static void CheckConfiguration()
    {
        Node3D actor = new();
        actor.AddChild(new NetworkObject { Name = "NetworkObject" });
        actor.AddChild(new Node3D { Name = "Socket" });
        actor.AddChild(new Node { Name = "Wrong" });
        PlayerCarrier carrier = new();
        actor.AddChild(carrier);
        try
        {
            foreach ((string anchor, string policy) in new[] { ("", "Wrong"), ("../Missing", "Wrong"), ("../Wrong", "Wrong"), ("../Socket", ""), ("../Socket", "../Missing"), ("../Socket", "../Wrong") })
            {
                carrier.AnchorPath = new NodePath(anchor); carrier.PermissionPath = new NodePath(policy);
                bool rejected = false;
                try { carrier._Ready(); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Invalid carry configuration allowed");
            }
        }
        finally { actor.Free(); }
        GD.Print("CARRY_PASS assertion=invalid_configuration");
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void StepRpc(int step, long hostId, long clientId, long itemId, long spareId, Vector3 dropPosition)
    {
        try
        {
            if (step == 0) await Until(() => _world.Count == 4, "Replica topology");
            Node3D item = Get(itemId);
            if (step == 9 || (_starter && step == 4))
            {
                long removed = _starter ? hostId : clientId;
                await Until(() => !Exists(removed) && Target(item).HolderNetworkObjectId == 0, "Holder despawn release replication");
                GD.Print("CARRY_PASS assertion=holder_cleanup_replicated");
            }
            else
            {
                Node3D host = Get(hostId), client = Get(clientId);
                if (!_starter) Require(client.GetNodeOrNull<Node>("NetworkGasComponent") is null && client is CarryFixtureActor && item.GetType() == typeof(Node3D), "Custom actor/item independence");
                if (!_starter) Require(item.GetNode<CollisionShape3D>("ContactBody/ContactShape").GetParent() is CollisionObject3D,
                    "Custom collision must have a physics-body parent");
                PlayerCarrier own = Carrier(client);
                switch (step)
                {
                    case 0:
                        await Until(() => Target(item).HolderNetworkObjectId == hostId && Follows(item, host), "Host holder identity/follow");
                        Vector3 initial = Carrier(host).CarryAnchor.GlobalPosition;
                        RpcId(1, MethodName.BeginMotionRpc, 0);
                        await Until(() => Carrier(host).CarryAnchor.GlobalPosition.DistanceTo(initial) > .1f &&
                            (!_starter || Carrier(host).CarryAnchor.GlobalPosition.Y > initial.Y + .05f) && Follows(item, host), "Remote host movement/jump follow");
                        Require(item.GetMultiplayerAuthority() == 1, "Item server authority");
                        Carrier(host).RpcId(1, "RequestDropRpc");
                        own.RpcId(1, "RequestDropRpc");
                        GD.Print("CARRY_PASS assertion=remote_host_follow_custom_anchor");
                        break;
                    case 1:
                        await Until(() => Target(item).HolderNetworkObjectId == 0 && item.GlobalPosition.DistanceTo(dropPosition) < .05f, "Host authoritative drop replication");
                        Use(client).RequestInteraction(new NetworkObjectId(itemId));
                        await Until(() => Target(item).HolderNetworkObjectId == clientId && Follows(item, client), "Client pickup");
                        Vector3 start = own.CarryAnchor.GlobalPosition;
                        if (_starter) { Input.ActionPress("move_right"); Input.ActionPress("jump"); }
                        else RpcId(1, MethodName.BeginMotionRpc, 1);
                        try { await Until(() => own.CarryAnchor.GlobalPosition.DistanceTo(start) > .1f &&
                            (!_starter || own.CarryAnchor.GlobalPosition.Y > start.Y + .05f) && Follows(item, client), "Client owner movement/jump follow"); }
                        finally { Input.ActionRelease("move_right"); Input.ActionRelease("jump"); }
                        GD.Print("CARRY_PASS assertion=client_pickup_owner_motion_follow");
                        break;
                    case 2:
                        own.RequestDrop();
                        if (_starter) await Until(() => Target(item).HolderNetworkObjectId == 0 && !own.HasCarriedItem, "Client drop");
                        break;
                    case 3:
                        if (_starter) await Until(() => Target(item).HolderNetworkObjectId == hostId, "Default cleanup pickup");
                        else { own.RequestDrop(); await Until(() => Target(item).HolderNetworkObjectId == 0, "Client drop replication"); }
                        break;
                    case 4:
                        Use(client).RequestInteraction(new NetworkObjectId(itemId));
                        break;
                    case 5:
                        Use(client).RequestInteraction(new NetworkObjectId(itemId));
                        await Until(() => Target(item).HolderNetworkObjectId == clientId, "Pickup before neutral release");
                        break;
                    case 6:
                        await Until(() => Target(item).HolderNetworkObjectId == 0, "Neutral lifecycle release");
                        Use(client).RequestInteraction(new NetworkObjectId(spareId));
                        await Until(() => Target(Get(spareId)).HolderNetworkObjectId == clientId, "Spare pickup");
                        break;
                    case 7:
                        await Until(() => !Exists(spareId), "Item despawn");
                        own.RpcId(1, "RequestDropRpc");
                        break;
                    case 8:
                        Use(client).RequestInteraction(new NetworkObjectId(itemId));
                        await Until(() => Target(item).HolderNetworkObjectId == clientId, "Pickup before holder cleanup");
                        break;
                }
            }
            RpcId(1, MethodName.CompletedRpc, step);
        }
        catch (Exception error) { Fail(error); }
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginMotionRpc(int step)
    {
        if (!Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != _clientPeer || step != _step) return;
        if (_starter) { Input.ActionPress("move_right"); Input.ActionPress("jump"); }
        else (step == 0 ? _host : _client).Position += new Vector3(.4f, .2f, 0);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void CompletedRpc(int step)
    {
        try
        {
            Require(Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == _clientPeer && step == _step, "Scenario sender/order");
            string[] expected = step == 0 ? new[] { "carry:sender_is_not_player_owner", "carry:player_not_carrying" } :
                !_starter && step == 2 ? new[] { "carry:drop_permission_denied" } :
                !_starter && step == 4 ? new[] { "carry:pickup_permission_denied" } :
                !_starter && step == 7 ? new[] { "carry:carried_item_missing" } : Array.Empty<string>();
            Require(_rejects.SequenceEqual(expected), "Rejection mismatch " + string.Join(',', _rejects));
            GD.Print($"CARRY_PASS assertion=step_{step} reasons={string.Join(',', expected)}");
            if (step == 0)
            {
                Input.ActionRelease("move_right"); Input.ActionRelease("jump");
                Carrier(_host).RequestDrop();
                Require(Target(_item).HolderNetworkObjectId == 0, "Host drop");
                _dropPosition = _item.GlobalPosition;
                Vector3 expectedPosition = _host.GlobalPosition + _host.GlobalBasis * Carrier(_host).DropOffset;
                Require(_dropPosition.DistanceTo(expectedPosition) < .01f, "Server drop transform");
            }
            if (step == 1)
            {
                Require(Target(_item).HolderNetworkObjectId == _clientId, "Client owner identity");
                await Until(() => Follows(_item, _client), "Server remote-client held follow");
                if (!_starter) Policy().DropAllowed = false;
            }
            if (_starter)
            {
                if (step == 2)
                {
                    Require(Target(_item).HolderNetworkObjectId == 0 && !Carrier(_client).HasCarriedItem, "Default client drop");
                    Require(Carrier(_host).TryPickup(Target(_item)), "Default held cleanup setup");
                }
                if (step == 3) { _world.Despawn(new NetworkObjectId(_hostId)); await Until(() => !Exists(_hostId), "Default holder despawn"); }
                if (step == 4) { await FinishServer(); return; }
            }
            else
            {
                if (step == 2) { Require(Target(_item).HolderNetworkObjectId == _clientId, "Denied drop changed holder"); Policy().DropAllowed = true; }
                if (step == 3) Policy().PickupAllowed = false;
                if (step == 4) { Require(Target(_item).HolderNetworkObjectId == 0, "Denied pickup changed holder"); Policy().PickupAllowed = true; }
                if (step == 5) { Policy().DropAllowed = false; Carrier(_client).ReleaseHeldItem("fixture_lifecycle"); Require(!Carrier(_client).HasCarriedItem && Target(_item).HolderNetworkObjectId == 0, "Neutral release bypasses input policy"); Policy().DropAllowed = true; }
                if (step == 6) { _world.Despawn(new NetworkObjectId(_spareId)); await Until(() => !Exists(_spareId), "Held item despawn"); }
                if (step == 7) Require(!Carrier(_client).HasCarriedItem, "Stale cache not cleared");
                if (step == 8) { _world.Despawn(new NetworkObjectId(_clientId)); await Until(() => !Exists(_clientId), "Custom holder despawn"); Require(Target(_item).HolderNetworkObjectId == 0, "Holder cleanup did not release"); }
                if (step == 9) { await FinishServer(); return; }
            }
            Next(step + 1);
        }
        catch (Exception error) { Fail(error); }
    }
    private CarryFixturePolicy Policy() => _client.GetNode<CarryFixturePolicy>("Permissions");
    private async Task FinishServer()
    {
        foreach (NetworkObject obj in _world.Objects.ToArray()) _world.Despawn(obj.Id);
        await Until(() => _world.Count == 0, "Server world cleanup");
        RpcId(_clientPeer, MethodName.FinishRpc);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void FinishRpc()
    {
        try { await Until(() => _world.Count == 0, "Client world cleanup"); GD.Print("CARRY_BOUNDARY_PASS role=client"); _finished = true; RpcId(1, MethodName.FinishedRpc); }
        catch (Exception error) { Fail(error); }
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishedRpc()
    {
        if (!Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != _clientPeer) return;
        GD.Print("CARRY_BOUNDARY_PASS role=host"); _finished = true; GetTree().Quit();
    }
    private void OnDisconnected() { if (_finished) GetTree().Quit(); else Fail(new InvalidOperationException("Premature host disconnect")); }
    private bool Exists(long id) => _world.TryGet(new NetworkObjectId(id), out _);
    private Node3D Get(long id) => _world.TryGet(new NetworkObjectId(id), out NetworkObject? obj) ? (Node3D)obj!.Host : throw new InvalidOperationException("Missing fixture object");
    private static long Id(Node host) => host.GetNode<NetworkObject>("NetworkObject").Id.Value;
    private static ICarryTarget Target(Node host) => CarryCapabilities.Target(host)!;
    private static PlayerCarrier Carrier(Node host) => (PlayerCarrier)CarryCapabilities.Holder(host)!;
    private PlayerInteractor Use(Node host) => host.GetNode<PlayerInteractor>(_starter ? "PlayerInteractor" : "Use");
    private static bool Follows(Node3D item, Node actor) => item.GlobalPosition.DistanceTo(Carrier(actor).CarryAnchor.GlobalPosition) < .05f;
    private async Task Until(Func<bool> condition, string label)
    {
        ulong deadline = Time.GetTicksMsec() + 5000;
        while (!condition()) { Require(Time.GetTicksMsec() < deadline, label + " timed out"); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
    public override void _Process(double delta) { if (_deadline > 0 && Time.GetTicksMsec() >= _deadline) { if (_finished) GetTree().Quit(); else Fail(new TimeoutException("Carry scenario timeout")); } }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { GD.Print("CARRY_BOUNDARY_FAIL: " + error); _finished = true; GetTree().Quit(1); }
    public override void _ExitTree()
    {
        Input.ActionRelease("move_right"); Input.ActionRelease("jump");
        GameLog.EntryWritten -= Observe;
        if (_isHost) Multiplayer.PeerConnected -= StartScenario; else Multiplayer.ServerDisconnected -= OnDisconnected;
        if (_peer is not null) { _peer.Close(); Multiplayer.MultiplayerPeer = null; _peer.Dispose(); }
    }
}
