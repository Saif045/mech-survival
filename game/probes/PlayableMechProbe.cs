using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Shell;
using MechSurvival.Mech;
using MechSurvival.Mech.Gas;
using MechSurvival.Player;

namespace MechSurvival.Probes;

/// <summary>Local lifecycle acceptance above ENet, using the configured production world.</summary>
public partial class PlayableMechProbe : Node
{
    public const int Port = 24874;
    private OnlineGameplayWorld _gameplay = null!;
    private NetworkWorld _world = null!;
    private ENetMultiplayerPeer? _peer;
    private bool _client, _connected, _hello, _finished, _busy;
    private int _ack;
    private string? _failure;
    private long _remote;
    private long _oldMax;
    private string[] _oldParts = [];
    private float _hostPeak;
    private Node TimeNode => GetNode("/root/NetworkTime");
    private PlayableMech[] Bodies => _world.Objects.Select(o => o.Host).OfType<PlayableMech>().OrderBy(b => Object(b).Id.Value).ToArray();
    private static NetworkObject Object(PlayableMech b) => b.GetNode<NetworkObject>("NetworkObject");
    public sealed record Sample(long Id, long Owner, float X, float Y, float Z, string Composition, string Gas);

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _Process(double delta)
    {
        if (_world is null) return;
        var host = Bodies.FirstOrDefault(b => Object(b).OwnerPeerId == PeerId.Server);
        if (host is not null && host.GetNode("Simulation").Get("grounded").AsBool())
            _hostPeak = Math.Max(_hostPeak, host.Position.Y);
        else if (host is not null && host.Position.Y < 1.9f)
            _hostPeak = Math.Max(_hostPeak, host.Position.Y);
    }

    private async void Run()
    {
        int result = 1;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            _client = args.Contains("--playable-client");
            bool offline = args.Contains("--playable-offline");
            bool fieldParts = args.Contains("--field-parts");
            bool service = args.Contains("--service");
            bool salvage = args.Contains("--salvage");
            Require(offline || _client || args.Contains("--playable-host"), "Select an explicit probe role.");
            // Exercise real interpolation even with the headless display driver.
            GetNode("/root/InterpolationServer").Call("set_server_enabled", true);
            string path = ProjectSettings.GetSetting("maaacks_game_template/game_scene_path").AsString();
            Require(path == "res://game/shell/online_gameplay.tscn", "Normal game is configured to a different scene.");
            var flow = GetNode<GameShell>("/root/GameShell");
            Require(flow.FallbackGameplayScenePath == path, "Fallback must select the game-owned scene.");
            Node menu = GD.Load<PackedScene>(GameShell.MainMenuScenePath).Instantiate();
            Require(flow.ResolveOnlineGameplayScenePath() == path && menu.Call("get_game_scene_path").AsString() == path,
                "Normal Host/Join routing does not resolve the configured mech scene.");
            menu.Free();
            GD.Print($"PLAYABLE_ROUTING_CONFIG_PASS host_join_invite_connect_target={path} candidate=bb0ca72 fallback={flow.FallbackGameplayScenePath}");
            Node shell = GD.Load<PackedScene>(path).Instantiate();
            Require(shell is OnlineGameplayShell, "Normal factory shell was replaced.");
            _gameplay = shell.GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
            Require(_gameplay.PlayerScene.ResourcePath == "res://game/player/playable_mech.tscn", "Normal PlayerScene is not the mech.");
            Require(_gameplay.GetNode(_gameplay.RoundContentPath) is MechSurvival.Shell.FieldPartsRoundContent, "Round content must be game-owned field parts.");
            Require(_gameplay.GetNode(_gameplay.SpawnDataProviderPath) is MechSurvival.Shell.MechSpawnDataProvider,
                "Spawn policy must be game-owned.");
            shell.RemoveChild(_gameplay); shell.Free();
            _gameplay.Name = "Gameplay";
            // Extract the configured world only: no Steam lobby/session is requested.
            // Shell composition, PlayerScene, floor and lifecycle remain production nodes.
            _peer = new ENetMultiplayerPeer();
            Multiplayer.ConnectedToServer += OnConnected;
            Require((_client ? _peer.CreateClient("127.0.0.1", Port) : _peer.CreateServer(offline ? 0 : Port)) == Error.Ok, "ENet startup failed.");
            Multiplayer.MultiplayerPeer = _peer;
            AddChild(_gameplay);
            _world = _gameplay.GetNode<NetworkWorld>("NetworkWorld");
            GD.Print($"PLAYABLE_NORMAL_GAME_PASS scene={path} player_scene={_gameplay.PlayerScene.ResourcePath}");
            if (_client)
            {
                _gameplay.Start();
                await Until(() => _connected);
                RpcId(1, MethodName.HelloRpc);
                await Until(() => _finished, 90000);
                GD.Print("PLAYABLE_CLIENT_PASS bidirectional=true guards=true rounds=2");
            }
            else if (salvage)
            {
                await SalvageAcceptance(offline);
                GD.Print(offline ? "PLAYABLE_OFFLINE_PASS" : "PLAYABLE_SERVER_PASS");
            }
            else if (service)
            {
                await ServiceAcceptance(offline);
                GD.Print(offline ? "PLAYABLE_OFFLINE_PASS" : "PLAYABLE_SERVER_PASS");
            }
            else if (fieldParts)
            {
                await FieldAcceptance(offline);
                GD.Print(offline ? "PLAYABLE_OFFLINE_PASS" : "PLAYABLE_SERVER_PASS");
            }
            else
            {
                GD.Print("PLAYABLE_READY role=server");
                if (!offline) await Until(() => _hello);
                StartRound();
                CheckFresh(offline ? 1 : 2);
                CheckRegistry();
                if (!offline) await Stage(1, "fresh");
                await Settle();
                Vector3[] initial = Bodies.Select(b => b.Position).ToArray();
                await DriveOwner("move_left");
                await Settle();
                Require(Bodies[0].Position.X < initial[0].X - 1, "Host input did not move host mech.");
                if (!offline) Require(PlanarDistance(initial[1], Bodies[1].Position) < 0.15f, "Host input drove client mech.");
                GD.Print($"PLAYABLE_HOST_MOTION_PASS from={initial[0]} to={Bodies[0].Position}");
                if (!offline) await Stage(2, "host_motion");
                await JumpOwner();
                await Settle();
                if (!offline)
                {
                    await Stage(3, "host_jump");
                    Vector3 hostBefore = Bodies[0].Position;
                    Vector3 clientBefore = Bodies[1].Position;
                    await Stage(4, "client_move");
                    await Settle();
                    Require(Bodies[1].Position.Z < clientBefore.Z - 1, "Client input did not move client mech on server.");
                    Require(PlanarDistance(hostBefore, Bodies[0].Position) < 0.15f, "Client input drove host mech.");
                    GD.Print($"PLAYABLE_CLIENT_MOTION_PASS from={clientBefore} to={Bodies[1].Position}");
                    await Stage(5, "client_motion");
                    float y = Bodies[1].Position.Y;
                    RpcId(_remote, MethodName.CommandRpc, 6, "client_jump", "");
                    await Until(() => Bodies[1].Position.Y > y + 0.2f);
                    await Until(() => _ack == 6);
                    await Settle();
                    await Stage(7, "client_jump_observed");
                }
                await MobilityAcceptance(offline);
                RememberRound();
                if (!offline) RpcId(_remote, MethodName.CommandRpc, 8, "reset", "");
                await ResetAndCheck();
                if (!offline) await Until(() => _ack == 8);
                StartRound();
                CheckFresh(offline ? 1 : 2);
                CheckRegistry();
                Require(Bodies.All(b => Object(b).Id.Value > _oldMax), "World object identity was reused.");
                Require(Bodies.SelectMany(b => b.View.Parts).All(p => !_oldParts.Contains(p.Id.ToString())), "Part identity was reused.");
                if (!offline) await Stage(9, "fresh_second_round");
                await Settle();
                // New owner's input must also work after Netfox deregistration/re-registration.
                float before = Bodies[0].Position.X;
                await DriveOwner("move_left"); await Settle();
                Require(Bodies[0].Position.X < before - 1, "Fresh round lost Netfox motion.");
                if (!offline) await Stage(10, "fresh_motion");
                GD.Print("PLAYABLE_ROUND_REUSE_PASS fresh_parts=true fresh_resources=true monotonic_ids=true transport_same=true");
                if (!offline) RpcId(_remote, MethodName.CommandRpc, 11, "reset", "");
                await ResetAndCheck();
                if (!offline)
                {
                    await Until(() => _ack == 11);
                    RpcId(_remote, MethodName.FinishRpc);
                    await Until(() => Multiplayer.GetPeers().Length == 0);
                }
                GD.Print(offline ? "PLAYABLE_OFFLINE_PASS" : "PLAYABLE_SERVER_PASS bidirectional=true guards=true rounds=2");
            }
            result = 0;
        }
        catch (Exception error) { GD.PushError($"PLAYABLE_FAIL: {error}"); }
        finally
        {
            ReleaseInputs();
            if (_world is not null)
            {
                foreach (var obj in _world.Objects.ToArray()) obj.Host.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"PLAYABLE_CLEANUP objects={_world.Count}");
                if (_world.Count != 0) result = 1;
            }
            _gameplay?.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Multiplayer.ConnectedToServer -= OnConnected;
            // This probe initiates local ENet shutdown. Deliver its disconnect
            // lifecycle while the peer is attached so NetworkEvents stops Netfox
            // before a later frame can run time synchronization without a peer.
            _peer?.Close();
            Multiplayer.EmitSignal(MultiplayerApi.SignalName.ServerDisconnected);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Multiplayer.MultiplayerPeer = null;
            _peer?.Dispose();
            GetTree().Quit(result);
        }
    }

    private void StartRound()
    {
        _gameplay.Start();
        // Probe-only pause makes canonical comparisons exact. Production keeps
        // the accepted idle drain; movement adds no resource cost or heat.
        foreach (var body in Bodies) body.Gas.SetResourceSimulationEnabled(false);
    }
    private void CheckRegistry()
    {
        var registry = (PlayerRegistry)typeof(OnlineGameplayWorld).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_gameplay)!;
        Require(registry.Count == Bodies.Length, "Lifecycle did not track initial operated bodies.");
        foreach (var player in registry.Players)
            Require(_world.TryGet(player.ObjectId, out var obj) && obj!.Host is PlayableMech && obj.OwnerPeerId == player.PeerId,
                "PlayerId/PeerId/operated NetworkObject mapping is invalid.");
        GD.Print($"PLAYABLE_LIFECYCLE_PASS players={registry.Count}");
    }
    private void CheckFresh(int count)
    {
        Require(Bodies.Length == count, "Unexpected playable count.");
        foreach (var body in Bodies)
        {
            bool host = Object(body).OwnerPeerId == PeerId.Server;
            Require(body.InitializedBeforeTree && body.InitialSpawnPosition == new Vector3(host ? -2 : 2, 2, 0), "Incorrect shell spawn data.");
            Require(body.View.Parts.Count == 9 && body.View.Parts.All(p => p.Condition == 1), "Incomplete fresh loadout.");
            var gas = body.Gas.ReadSnapshot();
            Require(gas == new MechGasSnapshot(2, 10, 5, true, true, true, true, 10, 0), "Incorrect fresh native GAS/resources.");
            CheckBody(body);
        }
        GD.Print($"PLAYABLE_FRESH_PASS bodies={count} parts=9 energy=capacity heat=0");
    }
    private void CheckBody(PlayableMech body)
    {
        var obj = Object(body);
        Require(obj.SpawnGroup == NetworkSpawnGroupKind.Players && body.GetMultiplayerAuthority() == 1 &&
            body.GetNode("Simulation").GetMultiplayerAuthority() == 1 && body.GetNode("Input").GetMultiplayerAuthority() == obj.OwnerPeerId.Value,
            "Incorrect player authority topology.");
        Require(body.GetNode<Camera3D>("Presentation/Camera3D").Current == body.GetNode("Input").IsMultiplayerAuthority(), "Incorrect owner camera.");
        var rb = body.GetNode("RollbackSynchronizer");
        Require(rb.Get("state_properties").AsGodotArray().Select(v => v.AsString()).SequenceEqual(new[] { ":position", ":velocity", "Simulation:grounded" }) &&
            rb.Get("input_properties").AsGodotArray().Select(v => v.AsString()).SequenceEqual(new[] { "Input:movement", "Input:jump_pressed" }) &&
            rb.Get("enable_prediction").AsBool() && !rb.Get("enable_input_broadcast").AsBool(), "Rollback contract changed.");
        Require(body.Gas.UsesRealAsc && body.Gas.ReadSnapshot() == body.Gas.Snapshot && body.Gas.Snapshot.Serialize() == body.GasState, "Native GAS is not canonical.");
        Require(body.GetNodeOrNull("NetworkGasComponent") is null, "Sample player GAS was imported.");
    }
    private async Task DriveOwner(string action)
    {
        Input.ActionPress(action);
        await Ticks(24);
        Input.ActionRelease(action);
    }
    private async Task MobilityAcceptance(bool offline)
    {
        // All mutations are authoritative probe setup, not repair/gameplay RPCs.
        string[] labels = ["pristine", "partial", "one_leg", "modifier", "modifier_damage", "modifier_removed", "modifier_reapplied", "modifier_gate", "zero", "restored"];
        float[] mobility = [2, 1.5f, 1, 1.5f, 1, 0.5f, 1, 0.5f, 0, 2];
        float[] structural = [2, 1.5f, 1, 1, 0.5f, 0.5f, 0.5f, 0, 0, 2];
        float[] speed = [6, 4.5f, 3, 4.5f, 3, 1.5f, 3, 0, 0, 6];
        for (int i = 0; i < labels.Length; i++)
        {
            foreach (var body in Bodies)
            {
                switch (i)
                {
                    case 1: body.ApplyDamage(MechSlot.LeftLeg, 0.5f); break;
                    case 2: body.ApplyDamage(MechSlot.LeftLeg, 0.5f); break;
                    case 3: body.Gas.SetDevelopmentModifier(true); break;
                    case 4: body.ApplyDamage(MechSlot.RightLeg, 0.5f); break;
                    case 5: body.Gas.SetDevelopmentModifier(false); break;
                    case 6: body.Gas.SetDevelopmentModifier(true); break;
                    case 7: body.ApplyDamage(MechSlot.RightLeg, 0.5f); break;
                    case 8: body.Gas.SetDevelopmentModifier(false); break;
                    case 9:
                        body.SetCondition(MechSlot.LeftLeg, 1);
                        body.SetCondition(MechSlot.RightLeg, 1);
                        break;
                }
                Require(body.View.SumEffectiveContribution(PartStat.Mobility) == structural[i] &&
                    body.GasMobility == mobility[i] && body.GasHasMobility == (i != 7 && i != 8) &&
                    body.GasMoveSpeed == speed[i] && body.Gas.HasDevelopmentModifier == (i == 3 || i == 4 || i == 6 || i == 7),
                    $"{labels[i]} canonical Mobility/modifier/capability mismatch.");
            }
            await Settle();
            if (!offline) await Stage(20 + i * 3, "mobility_state_" + labels[i]);
            Vector3[] before = Bodies.Select(b => b.Position).ToArray();
            await MeasureOwner(labels[i], "move_left");
            await Settle();
            CheckDisplacement(Bodies[0], before[0], speed[i], labels[i], "server_host");
            if (!offline)
            {
                Require(PlanarDistance(before[1], Bodies[1].Position) < 0.15f, "Host Mobility input drove remote owner.");
                before = Bodies.Select(b => b.Position).ToArray();
                RpcId(_remote, MethodName.CommandRpc, 21 + i * 3, "mobility_move_" + labels[i], "");
                await Until(() => _ack == 21 + i * 3);
                await Ticks(12);
                await Settle();
                CheckDisplacement(Bodies[1], before[1], speed[i], labels[i], "server_client");
                Require(PlanarDistance(before[0], Bodies[0].Position) < 0.15f, "Client Mobility input drove host owner.");
                await Stage(22 + i * 3, "mobility_result_" + labels[i]);
            }
            if (i == 7)
            {
                // No leg/jump capability rule is introduced. Numeric Mobility
                // remains positive from the native modifier while the hard gate
                // stops horizontal motion; grounded jump still works unchanged.
                await JumpOwner(); await Settle();
            }
        }
        GD.Print($"MOBILITY_ACCEPTANCE_PASS cases=10 peers={(offline ? 1 : 2)} damage=true modifier_preserved=true hard_gate=true resources_unchanged=true jump_unchanged=true");
    }
    private async Task MeasureOwner(string label, string action)
    {
        var body = Bodies.Single(b => b.GetNode("Input").IsMultiplayerAuthority());
        Vector3 before = body.Position;
        var resources = body.Gas.ReadSnapshot();
        await DriveOwner(action);
        await Settle();
        CheckDisplacement(body, before, body.GasMoveSpeed, label, "owner_prediction");
        Require(body.Gas.ReadSnapshot().Energy == resources.Energy && body.Gas.ReadSnapshot().Heat == resources.Heat,
            "Movement changed Energy/Heat while resource timers were paused.");
    }
    private void CheckDisplacement(PlayableMech body, Vector3 before, float speed, string label, string observation)
    {
        float distance = PlanarDistance(before, body.Position);
        float duration = 24 * TimeNode.Get("ticktime").AsSingle();
        float expected = speed * duration;
        float tolerance = speed == 0 ? 0.05f : Math.Max(0.3f, expected * 0.1f);
        Require(Math.Abs(distance - expected) <= tolerance,
            $"{label}/{observation}: displacement {distance} differs from expected {expected} (speed {speed}).");
        GD.Print($"MOBILITY_MOVEMENT_PASS case={label} observation={observation} owner={Object(body).OwnerPeerId} mobility={body.GasMobility} capability={body.GasHasMobility} speed={speed} ticks=24 seconds={duration} distance={distance} expected={expected} tolerance={tolerance}");
    }
    private async Task JumpOwner()
    {
        var body = Bodies.Single(b => b.GetNode("Input").IsMultiplayerAuthority());
        int previous = body.GetNode("Input").Get("recorded_jump_edges").AsInt32();
        float y = body.Position.Y;
        Input.ActionPress("jump");
        await Until(() => body.Position.Y > y + 0.2f);
        // Held semantic action must not record repeated pulses.
        await Ticks(8); Input.ActionRelease("jump");
        Require(body.GetNode("Input").Get("recorded_jump_edges").AsInt32() == previous + 1, "Jump was not one recorded input edge.");
        Require(body.Position.Y - y > 0.55f && body.Position.Y - y < 0.95f, "Jump trajectory changed.");
        GD.Print($"PLAYABLE_JUMP_PASS owner={Object(body).OwnerPeerId} recorded_edges=1 rise={body.Position.Y - y}");
    }
    private async Task Settle()
    {
        await Until(() => Bodies.Length > 0 && Bodies.All(b => b.GetNode("Simulation").Get("grounded").AsBool() && new Vector2(b.Velocity.X, b.Velocity.Z).Length() < 0.01f));
        await Ticks(12); // Allow ordinary state and interpolated presentation to converge.
    }
    private async Task Ticks(int count)
    {
        long target = TimeNode.Get("tick").AsInt64() + count;
        await Until(() => TimeNode.Get("tick").AsInt64() >= target);
    }
    private string Capture() => JsonSerializer.Serialize(Bodies.Select(b => new Sample(Object(b).Id.Value, Object(b).OwnerPeerId.Value,
        b.Position.X, b.Position.Y, b.Position.Z, b.Composition, b.GasState)));
    private async Task Stage(int stage, string command)
    {
        RpcId(_remote, MethodName.CommandRpc, stage, command, Capture());
        await Until(() => _ack == stage);
    }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private async void CommandRpc(int stage, string command, string samples)
    {
        if (!_client || _busy) { _failure = "Overlapping or invalid command."; return; }
        _busy = true;
        try
        {
            if (command.StartsWith("salvage_")) await SalvageCommand(command, samples);
            else if (command.StartsWith("service_")) await ServiceCommand(command, samples);
            else if (command.StartsWith("field_")) await FieldCommand(command, samples);
            else if (command == "reset") { RememberRound(); await ResetAndCheck(); }
            else if (command.StartsWith("mobility_move_")) await MeasureOwner(command[14..], "move_forward");
            else if (command == "client_move") await DriveOwner("move_forward");
            else if (command == "client_jump") await JumpOwner();
            else
            {
                var expected = JsonSerializer.Deserialize<Sample[]>(samples)!;
                bool fresh = command == "fresh" || command == "fresh_second_round";
                await Until(() => Matches(expected, fresh));
                foreach (var body in Bodies) { CheckBody(body); AssertGuards(body); }
                if (command.StartsWith("fresh"))
                {
                    if (command != "fresh_motion") CheckFresh(2);
                    if (command == "fresh_second_round")
                    {
                        Require(Bodies.All(b => Object(b).Id.Value > _oldMax), "Client observed reused object IDs.");
                        Require(Bodies.SelectMany(b => b.View.Parts).All(p => !_oldParts.Contains(p.Id.ToString())), "Client observed reused parts.");
                    }
                }
                if (command == "host_motion") _hostPeak = Bodies[0].Position.Y;
                if (command == "host_jump") Require(_hostPeak > Bodies[0].Position.Y + 0.2f, "Client did not observe physical host jump.");
                string marker = command.StartsWith("mobility_") ? "MOBILITY_OBSERVED" : "PLAYABLE_OBSERVED";
                GD.Print($"{marker} stage={stage} command={command} converged=true native_gas=true camera=true guards_per_body=19");
            }
            RpcId(1, MethodName.AckRpc, stage, "");
        }
        catch (Exception error) { _failure = error.ToString(); RpcId(1, MethodName.AckRpc, stage, _failure); }
        finally { _busy = false; }
    }
    private bool Matches(Sample[] expected, bool fresh)
    {
        if (Bodies.Length != expected.Length) return false;
        return expected.All(s => Bodies.Any(b => Object(b).Id.Value == s.Id && Object(b).OwnerPeerId.Value == s.Owner &&
            b.Composition == s.Composition && b.GasState == s.Gas && b.Gas.ReadSnapshot().Serialize() == s.Gas &&
            (fresh || (b.Position.DistanceTo(new(s.X, s.Y, s.Z)) < 0.2f &&
            b.GetNode<Node3D>("Presentation").GlobalPosition.DistanceTo(b.GlobalPosition) < 0.25f))));
    }
    private void RememberRound()
    {
        _oldMax = _world.Objects.Max(o => o.Id.Value);
        _oldParts = Bodies.SelectMany(b => b.View.Parts).Select(p => p.Id.ToString()).ToArray();
    }
    private async Task ResetAndCheck()
    {
        var old = Bodies.Select(b => (Body: b, Gas: b.Gas)).ToArray();
        long nextIdentity = GetNode("/root/NetworkIdentityServer").Get("_next_id").AsInt64();
        await _gameplay.ResetRoundAsync();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_gameplay.IsEmpty && _world.Count == 0 && old.All(p => !GodotObject.IsInstanceValid(p.Body) &&
            p.Body.View.Parts.Count == 0 && p.Gas.CleanedUp && p.Gas.Snapshot == default && !p.Gas.ResourceSimulationEnabled && p.Gas.ExternalHeatLoad == 0), "Round retained mech/GAS/resources.");
        Require(ReferenceEquals(Multiplayer.MultiplayerPeer, _peer), "Round replaced transport.");
        Require(GetNode("/root/NetworkIdentityServer").Get("_identifiers").AsGodotDictionary().Count == 0 &&
            GetNode("/root/RollbackSimulationServer").Get("_callbacks").AsGodotDictionary().Count == 0 &&
            GetNode("/root/NetworkHistoryServer").Get("_rb_state_properties").AsGodotObject().Call("get_subjects").AsGodotArray().Count == 0 &&
            GetNode("/root/NetworkHistoryServer").Get("_rb_input_properties").AsGodotObject().Call("get_subjects").AsGodotArray().Count == 0 &&
            GetNode("/root/InterpolationServer").Get("_properties").AsGodotObject().Call("get_subjects").AsGodotArray().Count == 0,
            "Netfox retained playable registrations.");
        Require(GetNode("/root/NetworkIdentityServer").Get("_next_id").AsInt64() >= nextIdentity, "Netfox allocator reset.");
        GD.Print($"PLAYABLE_TEARDOWN_PASS bodies={old.Length} objects=0 gas=0 resources=0 netfox=0 transport_same=true");
    }
    private void AssertGuards(PlayableMech body)
    {
        var part = new PartInstance(new PartDefinition("debug_leg", [MechSlot.LeftLeg, MechSlot.RightLeg]));
        Action[] mutations = [() => body.CreatePart("debug_leg"), () => body.AttachPart(MechSlot.LeftLeg, part),
            () => body.ReplacePart(MechSlot.LeftLeg, part, out _), () => body.DetachPart(MechSlot.LeftLeg),
            () => body.SetCondition(MechSlot.LeftLeg, 0), () => body.ApplyDamage(MechSlot.LeftLeg, 1), () => body.ResetBody(),
            () => _world.Spawn(_gameplay.PlayerScene), () => body.Gas.ReconcileStructure(), () => body.Gas.SetDevelopmentModifier(true),
            () => body.Gas.SetDevelopmentModifier(false), () => body.PublishGasSnapshot(default), () => body.Gas.InitializeEnergyFull(),
            () => body.Gas.SetResourcesForProbe(new(1, 50)), () => body.Gas.SetExternalHeatLoad(100), () => body.Gas.AdvanceResources(1),
            () => body.Gas.SetResourceSimulationEnabled(true), () => body.Gas.ConfigureResources(new(100, 1, 1)), () => body.Gas.ResetResourceState()];
        string before = body.Composition + body.GasState;
        foreach (string property in new[] { nameof(PlayableMech.GasMobility), nameof(PlayableMech.GasHasMobility), nameof(PlayableMech.GasMoveSpeed) })
            Require(typeof(PlayableMech).GetProperty(property)!.SetMethod is null, "Movement projection has a writable API.");
        foreach (var mutation in mutations)
        {
            bool rejected = false;
            try { mutation(); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Client mutation was accepted.");
        }
        Require(before == body.Composition + body.GasState && !body.Gas.ResourceSimulationEnabled, "Guard changed canonical state.");
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void HelloRpc() { if (Multiplayer.IsServer()) { _remote = Multiplayer.GetRemoteSenderId(); _hello = true; } }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AckRpc(int stage, string error) { if (Multiplayer.IsServer() && Multiplayer.GetRemoteSenderId() == _remote) { _ack = stage; if (error.Length > 0) _failure = error; } }
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void FinishRpc() => _finished = true;
    private void OnConnected() => _connected = true;
    private static float PlanarDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
    private static void ReleaseInputs() { foreach (string action in new[] { "move_left", "move_right", "move_forward", "move_backward", "jump", "interact", "drop_item", "service_part", "service_next_slot", "hack_1", "hack_2", "hack_3", "hack_4" }) Input.ActionRelease(action); }
    private async Task Until(Func<bool> condition, ulong timeout = 10000)
    {
        ulong deadline = Time.GetTicksMsec() + timeout;
        while (!condition())
        {
            Require(_failure is null, _failure ?? "");
            Require(Time.GetTicksMsec() < deadline, "Playable assertion timed out.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Require(_failure is null, _failure ?? "");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
