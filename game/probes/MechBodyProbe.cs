using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.World;
using MechSurvival.Mech;
using MechSurvival.Mech.Gas;

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
    private string _expectedGas = "";
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
        AddButton(column, "Development only: add GAS Mobility +0.5", () => NeedBody().Gas.SetDevelopmentModifier(true));
        AddButton(column, "Remove development GAS modifier", () => NeedBody().Gas.SetDevelopmentModifier(false));
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
            body.Gas.Changed += RefreshSlots;
            RefreshSlots();
        }
        if (body is null && _observed is not null)
        {
            _observed = null;
            RefreshSlots();
        }
        // Authority-only test RPC selects the expected observation; state still travels through the synchronizer.
        if (_scripted && _client && _pendingStage > _ack && body?.Composition == _expected &&
            body.View.Serialize() == _expected && body.GasState == _expectedGas && body.Gas.Snapshot.Serialize() == _expectedGas)
        {
            try
            {
                string structure = DescribeStructure(body.View);
                Require(structure == _expectedStructure, "Client structural output differs from the server.");
                Require(body.Gas.UsesRealAsc && body.Gas.ReadSnapshot() == body.Gas.Snapshot, "Client native GAS mirror differs.");
                AssertClientGuards(body);
                _ack = _pendingStage;
                GD.Print($"MECH_OBSERVED stage={_ack} composition={body.Composition} structure={structure} gas={body.Gas.ReadSnapshot().Serialize()} real_asc=true");
                RpcId(1, MethodName.AckRpc, _ack, body.View.Serialize(), structure, body.Gas.ReadSnapshot().Serialize());
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
        if (Body is { } current) _slots.AddChild(new Label { Text =
            $"GAS canonical/mirror: {current.Gas.Snapshot.Serialize()}\nReal ASC: {current.Gas.UsesRealAsc}; local development effect: {current.Gas.HasDevelopmentModifier}" });
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
                RunGasOfflineChecks();
                GD.Print("MECH_OFFLINE_PASS"); result = 0;
            }
            else if (_client)
            {
                await Until(() => _connected);
                RpcId(1, MethodName.HelloRpc);
                await Until(() => _finished, 25000);
                var old = NeedBody();
                var oldGas = old.Gas;
                await ClearWorld();
                Require(_world.Count == 0 && old.View.Parts.Count == 0, "Disconnect left stale client parts.");
                Require(oldGas.CleanedUp && oldGas.Snapshot == default && oldGas.ReadSnapshot() == default, "Disconnect left stale GAS mirror.");
                GD.Print("MECH_GAS_CLIENT_PASS real_asc=true mirror=true snapshots=12 canonical_late_sync=true cleanup=true");
                GD.Print("MECH_CLIENT_PASS disconnect_clear=true"); result = 0;
            }
            else
            {
                await RunGasServerChecks();
                result = 0;
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

    private static void CheckGas(NetworkMechBody body)
    {
        var gas = body.Gas;
        Require(gas.UsesRealAsc, "Mech does not contain the real vendored ASC or imported player attributes.");
        var actual = gas.ReadSnapshot();
        var input = MechGasSnapshot.FromStructure(body.View);
        Near(input.Mobility + (gas.HasDevelopmentModifier ? 0.5f : 0), actual.Mobility, "Native GAS Mobility mismatch.");
        Near(input.PowerCapacity, actual.PowerCapacity, "Native GAS PowerCapacity mismatch.");
        Near(input.CoolingCapacity, actual.CoolingCapacity, "Native GAS CoolingCapacity mismatch.");
        Require(actual.HasMobility == input.HasMobility && actual.HasSensor == input.HasSensor &&
            actual.HasPower == input.HasPower && actual.HasCooling == input.HasCooling, "Native GAS capability tags mismatch.");
        Require(actual == gas.Snapshot && actual.Serialize() == body.GasState, "Snapshot was not captured from native GAS.");
    }

    private void RunGasOfflineChecks()
    {
        var body = NeedBody(); var gas = body.Gas;
        CheckGas(body);
        body.AttachPart(MechSlot.LeftLeg, body.CreatePart("debug_leg"));
        body.AttachPart(MechSlot.RightLeg, body.CreatePart("debug_leg"));
        CheckGas(body); Near(2, gas.ReadSnapshot().Mobility, "Two-leg baseline mismatch.");
        body.ApplyDamage(MechSlot.LeftLeg, 0.25f); CheckGas(body);
        Near(1.75f, gas.ReadSnapshot().Mobility, "Partial GAS baseline mismatch.");
        Require(gas.SetDevelopmentModifier(true), "Native development GameplayEffect was not applied.");
        CheckGas(body); Near(2.25f, gas.ReadSnapshot().Mobility, "Native modifier output mismatch.");
        body.ApplyDamage(MechSlot.LeftLeg, 0.25f);
        Require(gas.HasDevelopmentModifier, "Baseline refresh erased the unrelated native effect.");
        CheckGas(body); Near(2, gas.ReadSnapshot().Mobility, "Baseline/effect coexistence failed.");
        Require(gas.SetDevelopmentModifier(false), "Native development effect was not removed.");
        CheckGas(body); Near(1.5f, gas.ReadSnapshot().Mobility, "Effect removal failed to restore current baseline.");
        body.ApplyDamage(MechSlot.LeftLeg, 1); CheckGas(body);
        Require(gas.ReadSnapshot().HasMobility, "One destroyed leg removed aggregate Mobility tag.");
        body.ApplyDamage(MechSlot.RightLeg, 1); CheckGas(body);
        Require(!gas.ReadSnapshot().HasMobility, "Mobility tag persisted without functioning legs.");
        body.ReplacePart(MechSlot.LeftLeg, body.CreatePart("debug_leg"), out _); CheckGas(body);
        Require(gas.ReadSnapshot().HasMobility, "Replacement did not restore Mobility tag.");
        body.AttachPart(MechSlot.Sensor, body.CreatePart("debug_sensor", 0.2f)); CheckGas(body);
        body.ApplyDamage(MechSlot.Sensor, 1); CheckGas(body);
        Require(!gas.ReadSnapshot().HasSensor, "Destroyed Sensor retained its native tag.");
        body.ReplacePart(MechSlot.Sensor, body.CreatePart("debug_sensor"), out _); CheckGas(body);
        Require(gas.ReadSnapshot().HasSensor, "Replacement did not restore native Sensor tag.");
        body.AttachPart(MechSlot.Power, body.CreatePart("debug_power", 0.5f)); CheckGas(body);
        body.ApplyDamage(MechSlot.Power, 0.25f); CheckGas(body);
        Near(2.5f, gas.ReadSnapshot().PowerCapacity, "Power baseline refresh failed.");
        body.AttachPart(MechSlot.Cooling, body.CreatePart("debug_cooling")); CheckGas(body);
        body.SetCondition(MechSlot.Cooling, 0.4f); CheckGas(body);
        Near(2, gas.ReadSnapshot().CoolingCapacity, "SetCondition did not refresh Cooling GAS.");
        body.DetachPart(MechSlot.Power); CheckGas(body);
        Require(!gas.ReadSnapshot().HasPower, "Detach retained Power tag.");
        int publications = 0;
        void OnChanged() => publications++;
        gas.Changed += OnChanged;
        try
        {
            body.ApplyDamage(MechSlot.LeftLeg, 0);
            body.SetCondition(MechSlot.LeftLeg, 1);
            body.AttachPart(MechSlot.LeftArm, body.CreatePart("debug_arm"));
            Require(publications == 0, "Equivalent structural baseline caused GAS churn.");
        }
        finally { gas.Changed -= OnChanged; }
        body.ResetBody(); CheckGas(body);
        Require(gas.ReadSnapshot() == default, "Reset retained GAS structural state.");
        // Leave a real active effect on another body to verify destruction cleans its handle/tag too.
        var teardownBody = _world.Objects.Select(o => o.Host).OfType<NetworkMechBody>().First(b => !ReferenceEquals(b, body));
        teardownBody.Gas.SetDevelopmentModifier(true);
        CheckGas(teardownBody);
        GD.Print("MECH_GAS_OFFLINE_PASS real_asc=true attributes=Mobility,PowerCapacity,CoolingCapacity tags=true aggregate_capability=true additive_modifier_preserved=true modifier_removed=true no_churn=true reset=true");
    }

    private async Task RunGasServerChecks()
    {
        var body = NeedBody(); var gas = body.Gas;
        var first = body.CreatePart("debug_leg");
        body.AttachPart(MechSlot.LeftLeg, first);
        body.ApplyDamage(MechSlot.LeftLeg, 0.65f);
        gas.SetDevelopmentModifier(true);
        CheckGas(body);
        await Until(() => _hello);
        await Stage(1, body); // damaged + GAS-modified late synchronization
        body.ApplyDamage(MechSlot.LeftLeg, 0.10f);
        Require(gas.HasDevelopmentModifier, "Damage erased the independent GAS effect.");
        CheckGas(body); await Stage(2, body);
        Require(gas.SetDevelopmentModifier(false), "Modifier removal failed.");
        CheckGas(body); await Stage(3, body);
        body.AttachPart(MechSlot.RightLeg, body.CreatePart("debug_leg"));
        body.AttachPart(MechSlot.Sensor, body.CreatePart("debug_sensor"));
        body.AttachPart(MechSlot.Power, body.CreatePart("debug_power"));
        body.AttachPart(MechSlot.Cooling, body.CreatePart("debug_cooling"));
        CheckGas(body); await Stage(4, body);
        body.ApplyDamage(MechSlot.LeftLeg, 1);
        body.ApplyDamage(MechSlot.Sensor, 1);
        body.ApplyDamage(MechSlot.Power, 0.5f);
        body.ApplyDamage(MechSlot.Cooling, 0.6f);
        CheckGas(body); Require(gas.ReadSnapshot().HasMobility && !gas.ReadSnapshot().HasSensor, "Aggregate GAS tag transition failed.");
        await Stage(5, body);
        body.ApplyDamage(MechSlot.RightLeg, 1);
        CheckGas(body); Require(!gas.ReadSnapshot().HasMobility, "All destroyed legs retained Mobility tag.");
        await Stage(6, body);
        var replacement = body.CreatePart("debug_leg", 0.75f);
        Require(body.ReplacePart(MechSlot.LeftLeg, replacement, out var old) && ReferenceEquals(old, first), "Replacement identity mismatch.");
        body.ReplacePart(MechSlot.Sensor, body.CreatePart("debug_sensor"), out _);
        CheckGas(body); await Stage(7, body);
        Require(ReferenceEquals(body.DetachPart(MechSlot.LeftLeg), replacement), "Detach mismatch.");
        CheckGas(body); await Stage(8, body);
        body.SetCondition(MechSlot.Cooling, 0.2f);
        CheckGas(body); await Stage(9, body);
        body.ResetBody(); CheckGas(body); await Stage(10, body);
        _world.Despawn(body.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id);
        await Until(() => _world.Count == 0);
        Require(gas.CleanedUp && gas.Snapshot == default && body.View.Parts.Count == 0, "Old body retained GAS state.");
        var fresh = Spawn(); var freshGas = fresh.Gas;
        Require(fresh.View.Parts.Count == 0 && freshGas.ReadSnapshot() == default && !freshGas.HasDevelopmentModifier, "Fresh body inherited GAS state.");
        fresh.AttachPart(MechSlot.LeftLeg, fresh.CreatePart("debug_leg"));
        CheckGas(fresh); await Stage(11, fresh);
        freshGas.SetDevelopmentModifier(true);
        CheckGas(fresh); await Stage(12, fresh);
        Rpc(MethodName.FinishRpc);
        await Until(() => Multiplayer.GetPeers().Length == 0);
        _world.Despawn(fresh.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id);
        await Until(() => _world.Count == 0);
        Require(freshGas.CleanedUp && freshGas.Snapshot == default && !freshGas.HasDevelopmentModifier, "Teardown retained native GAS effect/tag/snapshot.");
        GD.Print("MECH_GAS_SERVER_PASS real_asc=true snapshots=12 canonical_late_sync=true additive_modifier_preserved=true tags=true client_authority_rejected=true cleanup=true");
        GD.Print("MECH_SERVER_PASS stages=12 client_guards=true damage=true reset=true disconnect_clear=true fresh_body=true");
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
        string gas = body.Gas.ReadSnapshot().Serialize();
        Require(body.Gas.UsesRealAsc && body.GasState == gas, "Published GAS state is not native canonical state.");
        GD.Print($"MECH_PUBLISHED stage={stage} composition={expected} structure={structure} gas={gas} real_asc=true");
        Rpc(MethodName.ExpectRpc, stage, expected, structure, gas);
        await Until(() => _ack == stage);
        Require(body.Composition == expected, "Client produced a conflicting authoritative composition.");
        Require(body.Gas.ReadSnapshot().Serialize() == gas && body.GasState == gas, "Client changed canonical server GAS state.");
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
        Reject(() => body.Gas.ReconcileStructure()); // baseline and structural tag reconciliation are server-only
        Reject(() => body.Gas.SetDevelopmentModifier(true));
        Reject(() => body.Gas.SetDevelopmentModifier(false));
        Reject(() => body.PublishGasSnapshot(new MechGasSnapshot(99, 99, 99, true, true, true, true)));
        Require(typeof(MechGasComponent).GetMethod("SetAttribute") is null && typeof(MechGasComponent).GetMethod("GrantTag") is null &&
            typeof(MechGasComponent).GetMethod("RemoveTag") is null &&
            typeof(NetworkMechBody).GetProperty(nameof(NetworkMechBody.GasState))!.SetMethod!.IsPrivate,
            "Client-visible arbitrary attribute/tag/snapshot API exists.");
        GD.Print($"MECH_CLIENT_GUARDS stage={_pendingStage} rejected=12 gas_authority=true");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Client mutation was accepted.");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HelloRpc() { if (Multiplayer.IsServer() && Multiplayer.GetPeers().Contains(Multiplayer.GetRemoteSenderId())) _hello = true; }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ExpectRpc(int stage, string expected, string structure, string gas) { _pendingStage = stage; _expected = expected; _expectedStructure = structure; _expectedGas = gas; }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AckRpc(int stage, string actual, string structure, string gas)
    {
        var body = Body;
        if (Multiplayer.IsServer() && Multiplayer.GetPeers().Contains(Multiplayer.GetRemoteSenderId()) &&
            body is not null && body.Composition == actual && DescribeStructure(body.View) == structure &&
            body.Gas.ReadSnapshot().Serialize() == gas && stage == _ack + 1) _ack = stage;
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
        var old = _world.Objects.Select(obj => (Body: (NetworkMechBody)obj.Host, Gas: ((NetworkMechBody)obj.Host).Gas)).ToArray();
        foreach (var item in old) item.Body.QueueFree();
        if (old.Length > 0)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        foreach (var item in old)
            Require(item.Gas.CleanedUp && item.Gas.Snapshot == default && item.Body.View.Parts.Count == 0, "World teardown retained GAS/body state.");
        GD.Print($"MECH_GAS_CLEANUP cleared={old.Length} stale=false");
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
