using GameFactory.Starter.Gameplay.Interaction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;

namespace GameFactory.Sandbox.Interaction;

/// <summary>Two-process ENet acceptance of the production interaction RPC boundary.</summary>
public partial class InteractionBoundaryProbe : Node
{
    private ENetMultiplayerPeer _peer = null!;
    private NetworkWorld _world = null!;
    private Node3D _hostActor = null!;
    private Node3D _clientActor = null!;
    private InteractableSwitch _target = null!;
    private long _clientPeer;
    private int _step = -1;
    private bool _finished;
    private bool _isHost;
    private ulong _deadline;
    private readonly List<string> _rejections = new();

    public override void _Ready()
    {
        try
        {
            GameLog.EnsureInitialized();
            _peer = new ENetMultiplayerPeer();
            bool host = OS.GetCmdlineUserArgs().Contains("--interaction-host");
            _isHost = host;
            Error error = host ? _peer.CreateServer(24879, 1) : _peer.CreateClient("127.0.0.1", 24879);
            Require(error == Error.Ok, "ENet creation failed.");
            Multiplayer.MultiplayerPeer = _peer;
            _world = new NetworkWorld { Name = "NetworkWorld" };
            AddChild(_world);
            GameLog.EntryWritten += ObserveEntry;
            if (host)
            {
                Multiplayer.PeerConnected += StartScenario;
                GD.Print("INTERACTION_HOST_READY");
            }
            else
            {
                Multiplayer.ConnectedToServer += OnConnected;
                Multiplayer.ServerDisconnected += OnServerDisconnected;
            }
            _deadline = Time.GetTicksMsec() + 20000;
        }
        catch (Exception error) { Fail(error); }
    }

    private void ObserveEntry(LogEntry entry)
    {
        if (entry.Category == "interaction" && entry.Event == "rejected" && entry.Fields.TryGetValue("reason", out string? reason))
            _rejections.Add(reason!);
    }

    private void StartScenario(long peer)
    {
        try
        {
            _clientPeer = peer;
            CheckInvalidConfiguration();
            PackedScene actor = GD.Load<PackedScene>("res://factory/sandbox/interaction/custom_interaction_actor.tscn");
            _hostActor = _world.Spawn<Node3D>(actor, PeerId.Server, default(Variant));
            _clientActor = _world.Spawn<Node3D>(actor, new PeerId(peer), default(Variant));
            _target = _world.Spawn<InteractableSwitch>(
                GD.Load<PackedScene>("res://factory/starter/gameplay/interaction/interactable_switch.tscn"), PeerId.Server,
                new Godot.Collections.Dictionary { ["spawn_position"] = Vector3.Zero });
            Interactor(_hostActor).RequestInteraction(Id(_target));
            Require(_target.IsOn, "Host owner semantic request failed.");
            GD.Print("INTERACTION_PASS assertion=host_owner");
            NextStep(0);
        }
        catch (Exception error) { Fail(error); }
    }

    private static void CheckInvalidConfiguration()
    {
        PlayerInteractor interactor = new();
        Node wrongPolicy = new() { Name = "WrongPolicy" };
        interactor.AddChild(wrongPolicy);
        try
        {
            MethodInfo resolve = typeof(PlayerInteractor).GetMethod("ResolvePermission", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (string path in new[] { "", "Missing", "WrongPolicy" })
            {
                interactor.PermissionPath = new NodePath(path);
                try { resolve.Invoke(interactor, null); throw new InvalidOperationException("Invalid policy configuration granted permission."); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
            }
        }
        finally { interactor.Free(); } // Free also owns its child; Dispose alone does not free native nodes.
        GD.Print("INTERACTION_PASS assertion=invalid_policy_configuration");
    }

    private void NextStep(int step)
    {
        _step = step;
        _rejections.Clear();
        RpcId(_clientPeer, MethodName.RunStepRpc, step, Id(_hostActor).Value, Id(_clientActor).Value, Id(_target).Value);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void RunStepRpc(int step, long hostId, long clientId, long targetId)
    {
        try
        {
            await Until(() => _world.Count == 3, "Replicated custom composition");
            Node3D host = (Node3D)Get(hostId).Host;
            Node3D client = (Node3D)Get(clientId).Host;
            InteractableSwitch target = (InteractableSwitch)Get(targetId).Host;
            Require(client.GetNodeOrNull<Node>("NetworkGasComponent") is null && client.GetType() == typeof(Node3D),
                "Fixture acquired a starter player/GAS dependency.");
            PlayerInteractor own = Interactor(client);
            switch (step)
            {
                case 0:
                    await Until(() => target.IsOn, "Host mutation replication");
                    GD.Print("INTERACTION_PASS assertion=host_result_replicated");
                    own.RequestInteraction(new NetworkObjectId(targetId));
                    await Until(() => !target.IsOn, "Client mutation replication");
                    GD.Print("INTERACTION_PASS assertion=client_result_replicated");
                    break;
                case 1:
                    // Public entry suppresses non-owner calls; raw RPC proves the server also rejects spoofing.
                    Interactor(host).RequestInteraction(new NetworkObjectId(targetId));
                    Interactor(host).RpcId(1, "RequestInteractionRpc", targetId);
                    break;
                case 2:
                    own.RpcId(1, "RequestInteractionRpc", 0L);
                    own.RequestInteraction(new NetworkObjectId(999999));
                    break;
                case 3:
                case 4:
                    // Local actor position/policy remain permissive. Only the server has changed them.
                    own.RequestInteraction(new NetworkObjectId(targetId));
                    break;
                case 5:
                    own.RequestInteraction(new NetworkObjectId(clientId));
                    break;
                default: throw new InvalidOperationException("Unexpected step.");
            }
            // Same reliable channel as requests: the server checks after dispatch.
            RpcId(1, MethodName.StepCompletedRpc, step);
        }
        catch (Exception error) { Fail(error); }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StepCompletedRpc(int step)
    {
        try
        {
            Require(Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == _clientPeer && step == _step,
                "Invalid scenario completion sender/order.");
            Require(!_target.IsOn, "Rejected request mutated target.");
            string[] expected = step switch
            {
                0 => Array.Empty<string>(),
                1 => new[] { "sender_is_not_player_owner" },
                2 => new[] { "invalid_target_id", "target_not_found" },
                3 => new[] { "target_out_of_range" },
                4 => new[] { "actor_permission_denied" },
                5 => new[] { "self_interaction" },
                _ => throw new InvalidOperationException("Unexpected step.")
            };
            Require(_rejections.SequenceEqual(expected), $"Step {step} rejection mismatch: {string.Join(',', _rejections)}.");
            GD.Print($"INTERACTION_PASS assertion=step_{step} reasons={string.Join(',', expected)}");
            if (step == 2) _clientActor.Position = new Vector3(20, 0, 0);
            if (step == 3)
            {
                _clientActor.Position = Vector3.Zero;
                _clientActor.GetNode<FixtureInteractionPermission>("Permission").Allowed = false;
            }
            if (step == 4) _clientActor.GetNode<FixtureInteractionPermission>("Permission").Allowed = true;
            if (step < 5) NextStep(step + 1);
            else RpcId(_clientPeer, MethodName.FinishRpc);
        }
        catch (Exception error) { Fail(error); }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishRpc()
    {
        GD.Print("INTERACTION_BOUNDARY_PASS role=client");
        RpcId(1, MethodName.FinishedRpc);
        _finished = true;
        // Host exits after the acknowledgement; client exits on disconnect so the ack is flushed.
    }

    private void OnConnected() => GD.Print("INTERACTION_CLIENT_CONNECTED");
    private void OnServerDisconnected()
    {
        if (_finished) GetTree().Quit();
        else Fail(new InvalidOperationException("Host disconnected before acceptance completed."));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishedRpc()
    {
        if (!Multiplayer.IsServer() || Multiplayer.GetRemoteSenderId() != _clientPeer) return;
        GD.Print("INTERACTION_BOUNDARY_PASS role=host");
        _finished = true;
        GetTree().Quit();
    }

    private NetworkObject Get(long id) => _world.TryGet(new NetworkObjectId(id), out NetworkObject? value)
        ? value! : throw new InvalidOperationException("Fixture object missing.");
    private static NetworkObjectId Id(Node host) => host.GetNode<NetworkObject>("NetworkObject").Id;
    private static PlayerInteractor Interactor(Node host) => host.GetNode<PlayerInteractor>("PlayerInteractor");

    private async Task Until(Func<bool> condition, string assertion)
    {
        ulong deadline = Time.GetTicksMsec() + 5000;
        while (!condition())
        {
            Require(Time.GetTicksMsec() < deadline, assertion + " timed out.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    public override void _Process(double delta)
    {
        if (_deadline == 0 || Time.GetTicksMsec() < _deadline) return;
        if (!_finished) Fail(new TimeoutException("Interaction scenario timed out."));
        else GetTree().Quit();
    }

    private void Fail(Exception error)
    {
        GD.Print("INTERACTION_BOUNDARY_FAIL: " + error);
        _finished = true;
        GetTree().Quit(1);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    public override void _ExitTree()
    {
        GameLog.EntryWritten -= ObserveEntry;
        if (_isHost) Multiplayer.PeerConnected -= StartScenario;
        else
        {
            Multiplayer.ConnectedToServer -= OnConnected;
            Multiplayer.ServerDisconnected -= OnServerDisconnected;
        }
        if (_peer is not null) { _peer.Close(); Multiplayer.MultiplayerPeer = null; _peer.Dispose(); }
    }
}
