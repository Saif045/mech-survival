using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.World;
using MechSurvival.Mech;

namespace MechSurvival.Probes;

/// <summary>Game-owned inspection scene and bounded local two-peer acceptance scenario.</summary>
public partial class MechBodyProbe : Control
{
    public const int Port = 24873;
    private NetworkWorld _world = null!;
    private PackedScene _prefab = null!;
    private ENetMultiplayerPeer? _peer;
    private Label _status = null!;
    private VBoxContainer _slots = null!;
    private OptionButton _slot = null!;
    private OptionButton _model = null!;
    private SpinBox _condition = null!;
    private bool _client;
    private bool _scripted;
    private bool _connected;
    private bool _disconnected;
    private bool _signalsSubscribed;
    private bool _hello;
    private int _ack;
    private int _pendingStage;
    private string _expected = "";
    private string _expectedStructure = "";
    private bool _finished;
    private NetworkMechBody? _observed;

    private NetworkMechBody? Body => _world.Objects.Select(o => o.Host).OfType<NetworkMechBody>().FirstOrDefault();

    public override void _Ready()
    {
        _world = GetNode<NetworkWorld>("NetworkWorld");
        _prefab = GD.Load<PackedScene>("res://game/mech/mech_body.tscn");
        BuildUi();
        CallDeferred(MethodName.Run);
    }

    private void BuildUi()
    {
        var scroll = new ScrollContainer();
        AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = scroll.OffsetTop = 24;
        scroll.OffsetRight = scroll.OffsetBottom = -24;
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);
        column.AddChild(new Label { Text = "Modular mech body probe — server owns composition and condition" });
        _status = new Label { Text = "Starting…" }; column.AddChild(_status);
        _slot = new OptionButton();
        foreach (var slot in MechBody.Slots) _slot.AddItem(slot.ToString(), (int)slot);
        column.AddChild(_slot);
        _model = new OptionButton();
        foreach (string model in new[] { "debug_core", "debug_torso", "debug_sensor", "debug_arm", "debug_leg", "debug_power", "debug_cooling" })
            _model.AddItem(model);
        column.AddChild(_model);
        _condition = new SpinBox { MinValue = 0, MaxValue = 1, Step = 0.05, Value = 1 };
        column.AddChild(_condition);
        AddButton(column, "Spawn empty body", () => { Require(Body is null, "A body already exists."); Spawn(); });
        AddButton(column, "Attach selected model (mismatched slot is rejected)", () =>
            Require(NeedBody().AttachPart(SelectedSlot(), NewPart()), "Attach rejected: occupied, duplicate or incompatible."));
        AddButton(column, "Replace selected slot", () =>
            Require(NeedBody().ReplacePart(SelectedSlot(), NewPart(), out _), "Replacement rejected."));
        AddButton(column, "Detach selected slot", () => NeedBody().DetachPart(SelectedSlot()));
        AddButton(column, "Set selected slot condition", () =>
            Require(NeedBody().SetCondition(SelectedSlot(), (float)_condition.Value), "Slot is empty."));
        AddButton(column, "Damage selected part by 0.10", () => DamageSelected(0.10f));
        AddButton(column, "Damage selected part by 0.25", () => DamageSelected(0.25f));
        AddButton(column, "Destroy selected part", () => DamageSelected(1));
        AddButton(column, "Reset body", () => NeedBody().ResetBody());
        _slots = new VBoxContainer(); column.AddChild(_slots);
        column.AddChild(new Label { Text = "Clients observe only. Host/client flags use local ENet port 24873.\nRaw/effective values describe physical structure. GAS, movement, energy and heat remain deferred." });
    }

    private MechSlot SelectedSlot() => (MechSlot)_slot.GetSelectedId();
    private NetworkMechBody NeedBody() => Body ?? throw new InvalidOperationException("No body spawned.");
    private PartInstance NewPart() => NeedBody().CreatePart(_model.GetItemText(_model.Selected), (float)_condition.Value);
    private void DamageSelected(float amount)
    {
        var result = NeedBody().ApplyDamage(SelectedSlot(), amount);
        Require(result.PartId is not null, "Slot is empty.");
    }
    private void AddButton(VBoxContainer column, string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += () =>
        {
            try { action(); _status.Text = "Operation completed."; }
            catch (Exception error) { _status.Text = error.Message; }
        };
        column.AddChild(button);
    }

    public override void _Process(double delta)
    {
        var body = Body;
        if (body is not null && !ReferenceEquals(body, _observed))
        {
            _observed = body;
            body.Changed += RefreshSlots;
            RefreshSlots();
        }
        if (body is null && _observed is not null)
        {
            _observed = null;
            RefreshSlots();
        }
        // Authority-only test RPC selects the expected observation; state still travels through the synchronizer.
        if (_scripted && _client && _pendingStage > _ack && body?.Composition == _expected &&
            body.View.Serialize() == _expected)
        {
            try
            {
                string structure = DescribeStructure(body.View);
                Require(structure == _expectedStructure, "Client structural output differs from the server.");
                AssertClientGuards(body);
                _ack = _pendingStage;
                GD.Print($"MECH_OBSERVED stage={_ack} composition={body.Composition} structure={structure}");
                RpcId(1, MethodName.AckRpc, _ack, body.View.Serialize(), structure);
            }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        }
    }

    private void RefreshSlots()
    {
        foreach (var child in _slots.GetChildren()) { _slots.RemoveChild(child); child.QueueFree(); }
        var view = Body?.View;
        foreach (var slot in MechBody.Slots)
        {
            var part = view?.GetPart(slot);
            _slots.AddChild(new Label { Text = part is null ? $"{slot}: empty" :
                $"{slot}: {part.Definition.Model} | {part.Id.Value} | condition {part.Condition:0.00} | functioning {part.Functioning}\n" +
                string.Join(" | ", Enum.GetValues<PartStat>().Select(stat =>
                    $"{stat}: raw {part.GetContribution(stat):0.###}, effective {part.GetEffectiveContribution(stat):0.###}")) });
        }
        if (view is not null) _slots.AddChild(new Label { Text =
            $"Functioning mobility parts: {view.CountCapability(PartCapability.Mobility)}\n{DescribeStructure(view)}" });
    }

    private NetworkMechBody Spawn() => _world.Spawn<NetworkMechBody>(_prefab, _ => { });

    private async void Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _client = args.Contains("--mech-client");
        _scripted = args.Contains("--mech-acceptance") || args.Contains("--mech-offline-check");
        bool networked = _client || args.Contains("--mech-host");
        int result = 1;
        try
        {
            if (networked)
            {
                _peer = new ENetMultiplayerPeer();
                var error = _client ? _peer.CreateClient("127.0.0.1", Port) : _peer.CreateServer(Port);
                Require(error == Error.Ok, $"ENet setup: {error}");
                Multiplayer.ConnectedToServer += OnConnected;
                Multiplayer.ServerDisconnected += OnDisconnected;
                _signalsSubscribed = true;
                Multiplayer.MultiplayerPeer = _peer;
            }
            _status.Text = _client ? "Client: observing server state" : "Host/offline: authoritative state";
            GD.Print($"MECH_READY role={(_client ? "client" : "server")}");
            if (!_client) Spawn(); // Spawn before client joins: proves late-join composition too.
            if (!_scripted) return;
            if (args.Contains("--mech-offline-check"))
            {
                RunOfflineChecks();
                RunDamageOfflineChecks();
                GD.Print("MECH_OFFLINE_PASS"); result = 0;
            }
            else if (_client)
            {
                await Until(() => _connected);
                RpcId(1, MethodName.HelloRpc);
                await Until(() => _finished, 25000);
                var old = NeedBody();
                _peer!.Close();
                Multiplayer.MultiplayerPeer = null;
                await ClearWorld();
                Require(_world.Count == 0 && old.View.Parts.Count == 0, "Disconnect left stale client parts.");
                GD.Print("MECH_CLIENT_PASS disconnect_clear=true"); result = 0;
            }
            else
            {
                var body = NeedBody();
                var first = body.CreatePart("debug_leg");
                Require(body.AttachPart(MechSlot.LeftLeg, first), "Initial attach failed.");
                // Apply damage before the client starts: late synchronization must receive current damage.
                body.ApplyDamage(MechSlot.LeftLeg, 0.65f);
                await Until(() => _hello);
                Near(0.35f, body.GetPart(MechSlot.LeftLeg)!.Condition, "Late-join damage setup failed.");
                await Stage(1, body);
                var partial = body.ApplyDamage(MechSlot.LeftLeg, 0.10f);
                Require(partial.Changed && !partial.DestroyedByThisOperation, "Partial damage failed.");
                Near(0.25f, body.GetPart(MechSlot.LeftLeg)!.Condition, "Partial condition mismatch.");
                await Stage(2, body);
                var replacement = body.CreatePart("debug_leg", 0.75f);
                Require(body.ReplacePart(MechSlot.LeftLeg, replacement, out var previous) && ReferenceEquals(previous, first), "Replace mismatch.");
                Require(previous!.InstalledSlot is null && first.Id != replacement.Id, "Replacement identity/slot mismatch.");
                await Stage(3, body);
                Require(body.ApplyDamage(MechSlot.LeftLeg, 1).DestroyedByThisOperation, "Destroying damage failed.");
                Require(body.HasPart(MechSlot.LeftLeg), "Destroyed part vanished.");
                await Stage(4, body);
                Require(ReferenceEquals(body.DetachPart(MechSlot.LeftLeg), replacement), "Detach mismatch.");
                await Stage(5, body);
                body.AttachPart(MechSlot.Sensor, body.CreatePart("debug_sensor", 0.2f));
                body.AttachPart(MechSlot.LeftLeg, body.CreatePart("debug_leg", 0.4f));
                await Stage(6, body);
                body.ResetBody();
                await Stage(7, body);
                _world.Despawn(body.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id);
                await Until(() => _world.Count == 0);
                Require(body.View.Parts.Count == 0, "Despawn left stale server parts.");
                var fresh = Spawn();
                Require(fresh.View.Parts.Count == 0, "Fresh body retained prior state.");
                fresh.AttachPart(MechSlot.LeftLeg, fresh.CreatePart("debug_leg"));
                Near(1, fresh.GetPart(MechSlot.LeftLeg)!.Condition, "Fresh part retained old damage.");
                Near(1, fresh.View.SumEffectiveContribution(PartStat.Mobility), "Fresh structural output mismatch.");
                await Stage(8, fresh);
                Rpc(MethodName.FinishRpc);
                await Until(() => Multiplayer.GetPeers().Length == 0);
                _world.Despawn(fresh.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id);
                await Until(() => _world.Count == 0);
                Require(fresh.View.Parts.Count == 0, "Fresh body teardown left stale state.");
                GD.Print("MECH_SERVER_PASS stages=8 client_guards=true damage=true late_join_damaged=true reset=true disconnect_clear=true fresh_body=true"); result = 0;
            }
        }
        catch (Exception error) { GD.PushError($"MECH_FAIL: {error}"); }
        finally
        {
            if (_scripted)
            {
                await ClearWorld();
                Multiplayer.MultiplayerPeer = null;
                _peer?.Close();
                _peer?.Dispose();
                GD.Print($"MECH_CLEANUP objects={_world.Count}");
                GetTree().Quit(result);
            }
        }
    }

    private void RunOfflineChecks()
    {
        var body = NeedBody(); var part = body.CreatePart("debug_leg");
        Require(body.View.Slots.Count == 9 && body.View.Parts.Count == 0, "Empty body mismatch.");
        Require(!body.AttachPart(MechSlot.Sensor, part), "Incompatible part accepted.");
        Require(body.AttachPart(MechSlot.LeftLeg, part), "Valid attach failed.");
        Require(!body.AttachPart(MechSlot.RightLeg, part), "Duplicate part accepted.");
        var otherBody = Spawn();
        Require(!otherBody.AttachPart(MechSlot.RightLeg, part), "Cross-body duplicate accepted.");
        var next = body.CreatePart("debug_leg", 0.5f);
        Require(body.ReplacePart(MechSlot.LeftLeg, next, out var old) && ReferenceEquals(old, part), "Replace failed.");
        Require(otherBody.AttachPart(MechSlot.RightLeg, part), "Detached runtime part could not transfer bodies.");
        Require(body.SetCondition(MechSlot.LeftLeg, 0), "Condition failed.");
        Require(body.HasPart(MechSlot.LeftLeg) && body.View.CountCapability(PartCapability.Mobility) == 0, "Destroyed observation failed.");
        Require(ReferenceEquals(body.DetachPart(MechSlot.LeftLeg), next), "Detach failed.");
        Require(body.DetachPart(MechSlot.LeftLeg) is null, "Empty detach failed.");
        try { body.SetCondition(MechSlot.LeftLeg, float.NaN); throw new Exception("Invalid condition accepted."); }
        catch (ArgumentOutOfRangeException) { }
        body.ResetBody(); otherBody.ResetBody();
        Require(body.View.Parts.Count == 0 && otherBody.View.Parts.Count == 0, "Reset failed.");
    }

    private void RunDamageOfflineChecks()
    {
        var body = NeedBody();
        body.AttachPart(MechSlot.LeftLeg, body.CreatePart("debug_leg"));
        body.AttachPart(MechSlot.RightLeg, body.CreatePart("debug_leg"));
        var pristine = body.View;
        var unrelated = pristine.GetPart(MechSlot.RightLeg);
        Near(2, pristine.SumContribution(PartStat.Mobility), "Pristine raw output mismatch.");
        Near(2, pristine.SumEffectiveContribution(PartStat.Mobility), "Pristine effective output mismatch.");
        int publications = 0;
        void OnChanged() => publications++;
        body.Changed += OnChanged;
        try
        {
            var partial = body.ApplyDamage(MechSlot.LeftLeg, 0.25f);
            Require(partial.Changed && !partial.DestroyedByThisOperation, "Partial damage result mismatch.");
            Near(0.75f, partial.NewCondition, "Partial damage condition mismatch.");
            Near(2, body.View.SumContribution(PartStat.Mobility), "Damage changed raw output.");
            Near(1.75f, body.View.SumEffectiveContribution(PartStat.Mobility), "Partial effective output mismatch.");
            int beforeNoOps = publications;
            string beforeComposition = body.Composition;
            Require(!body.ApplyDamage(MechSlot.LeftLeg, 0).Changed, "Zero damage changed state.");
            Require(!body.ApplyDamage(MechSlot.LeftLeg, float.Epsilon).Changed, "Unrepresentable damage changed state.");
            Require(body.ApplyDamage(MechSlot.Sensor, 0.5f).PartId is null, "Empty damage returned a part.");
            foreach (float invalid in new[] { -0.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                try { body.ApplyDamage(MechSlot.LeftLeg, invalid); throw new Exception("Invalid damage accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            Require(publications == beforeNoOps && beforeComposition == body.Composition,
                "No-op or invalid damage unnecessarily published state.");
            var destroyed = body.ApplyDamage(MechSlot.LeftLeg, 1);
            Require(destroyed.DestroyedByThisOperation && body.HasPart(MechSlot.LeftLeg), "Destroyed part vanished.");
            Near(0, body.GetPart(MechSlot.LeftLeg)!.GetEffectiveContribution(PartStat.Mobility), "Destroyed effective output remains.");
            Near(2, body.View.SumContribution(PartStat.Mobility), "Destroyed raw output changed.");
            Near(1, body.View.SumEffectiveContribution(PartStat.Mobility), "Unrelated output changed.");
            Require(body.View.CountCapability(PartCapability.Mobility) == 1, "Destroyed capability still available.");
            Require(unrelated == body.GetPart(MechSlot.RightLeg), "Damage changed unrelated part.");
            Near(1, pristine.GetPart(MechSlot.LeftLeg)!.Condition, "Captured view mutated retroactively.");
            int beforeDestroyed = publications;
            Require(!body.ApplyDamage(MechSlot.LeftLeg, 0.5f).Changed && publications == beforeDestroyed,
                "Already destroyed part published damage.");
            GD.Print("MECH_DAMAGE_OFFLINE_PASS raw_preserved=true effective_linear=true capability_loss=true unrelated_stable=true invalid_rejected=true no_op_publications=0 immutable_view=true");
        }
        finally { body.Changed -= OnChanged; }
        body.ResetBody();
    }

    private static string DescribeStructure(MechBodyView view) =>
        string.Join(";", Enum.GetValues<PartStat>().Select(stat => FormattableString.Invariant(
            $"{stat}:raw={view.SumContribution(stat):R},effective={view.SumEffectiveContribution(stat):R}"))) +
        "|capabilities=" + string.Join(",", Enum.GetValues<PartCapability>().Select(cap => $"{cap}:{view.CountCapability(cap)}"));

    private static void Near(float expected, float actual, string message) =>
        Require(MathF.Abs(expected - actual) <= 0.000001f, message);

    private async Task Stage(int stage, NetworkMechBody body)
    {
        string expected = body.Composition;
        string structure = DescribeStructure(body.View);
        GD.Print($"MECH_PUBLISHED stage={stage} composition={expected} structure={structure}");
        Rpc(MethodName.ExpectRpc, stage, expected, structure);
        await Until(() => _ack == stage);
        Require(body.Composition == expected, "Client produced a conflicting authoritative composition.");
    }
    private void AssertClientGuards(NetworkMechBody body)
    {
        var part = new PartInstance(new PartDefinition("debug_leg", [MechSlot.LeftLeg, MechSlot.RightLeg]));
        Reject(() => body.CreatePart("debug_leg"));
        Reject(() => body.AttachPart(MechSlot.RightLeg, part));
        Reject(() => body.ReplacePart(MechSlot.LeftLeg, part, out _));
        Reject(() => body.DetachPart(MechSlot.LeftLeg));
        Reject(() => body.SetCondition(MechSlot.LeftLeg, 1));
        Reject(() => body.ApplyDamage(MechSlot.LeftLeg, 0.25f));
        Reject(() => body.ResetBody());
        Reject(() => _world.Spawn(_prefab));
        GD.Print($"MECH_CLIENT_GUARDS stage={_pendingStage} rejected=8");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Client mutation was accepted.");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HelloRpc() { if (Multiplayer.IsServer() && Multiplayer.GetPeers().Contains(Multiplayer.GetRemoteSenderId())) _hello = true; }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ExpectRpc(int stage, string expected, string structure) { _pendingStage = stage; _expected = expected; _expectedStructure = structure; }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AckRpc(int stage, string actual, string structure)
    {
        var body = Body;
        if (Multiplayer.IsServer() && Multiplayer.GetPeers().Contains(Multiplayer.GetRemoteSenderId()) &&
            body is not null && body.Composition == actual && DescribeStructure(body.View) == structure && stage == _ack + 1) _ack = stage;
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishRpc() => _finished = true;

    private void OnConnected() => _connected = true;
    private async void OnDisconnected()
    {
        _disconnected = true;
        if (!_scripted) { await ClearWorld(); _status.Text = "Disconnected: body state cleared."; }
    }
    private async Task ClearWorld()
    {
        foreach (var obj in _world.Objects.ToArray()) obj.Host.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Until(Func<bool> condition, ulong timeout = 10000)
    {
        ulong deadline = Time.GetTicksMsec() + timeout;
        while (!condition())
        {
            Require(Time.GetTicksMsec() < deadline, "Probe assertion timed out.");
            Require(!_client || !_disconnected || _finished, "Server disconnected before completion.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public override void _ExitTree()
    {
        if (_signalsSubscribed)
        {
            Multiplayer.ConnectedToServer -= OnConnected;
            Multiplayer.ServerDisconnected -= OnDisconnected;
            _signalsSubscribed = false;
        }
        if (!_scripted) { _peer?.Close(); _peer?.Dispose(); }
    }
}
