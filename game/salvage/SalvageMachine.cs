using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using MechSurvival.Mech;
using MechSurvival.Player;

namespace MechSurvival.Salvage;

public sealed record SalvageRewardPreview(string Model, float Condition);

public partial class SalvageMachine : Node3D, INetworkSpawnInitializable, IInteractable
{
    public const string ScenePath = "res://game/salvage/salvage_machine.tscn";
    public const float EncounterRange = 2.75f;
    public const double PreviewSeconds = 1.5, ResponseSeconds = 8, WarningSeconds = 2;
    public sealed record Observation(SalvageMachineState State, long OperatorId, int[] Sequence, int Progress, bool InputOpen);
    private static string EmptyState => JsonSerializer.Serialize(new Observation(SalvageMachineState.Neutral, 0, [], 0, false));
    [Export, Replicated(ReplicationMode.OnChange)] public string EncounterState { get; private set; } = EmptyState;
    public SalvageMachineState State { get; private set; }
    public long OperatorNetworkObjectId { get; private set; }
    public IReadOnlyList<int> Sequence { get; private set; } = Array.Empty<int>();
    public int Progress { get; private set; }
    public bool InputOpen { get; private set; }
    public IReadOnlyList<SalvageRewardPreview> Rewards { get; private set; } = Array.Empty<SalvageRewardPreview>();
    public NetworkObject NetworkObject => GetNode<NetworkObject>("NetworkObject");
    public bool CleanedUp { get; private set; }
    public bool HasActiveTimer => _previewRemaining > 0 || _responseRemaining > 0 || _warningRemaining > 0;
    private PartInstance[] _rewards = [];
    private SalvageChallenge? _challenge;
    private double _previewRemaining, _responseRemaining, _warningRemaining;
    private ReplicationComponent? _replication;
    private Label3D _status = null!;
    private Label _hud = null!;
    private StandardMaterial3D _material = null!;

    public static SalvageMachine Spawn(NetworkWorld world, Transform3D transform)
    {
        if (!world.Multiplayer.IsServer()) throw new InvalidOperationException("Only server gameplay creates salvage rewards.");
        var catalog = GD.Load<MechPartCatalogResource>("res://game/mech/definitions/catalog.tres").Freeze();
        PartInstance[] rewards = [new(catalog.Resolve("industrial_cooling"), 0.9f), new(catalog.Resolve("high_density_power"), 0.85f)];
        var preview = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var part in rewards) preview.Add(new() { ["model"] = part.Definition.Model, ["condition"] = part.Condition });
        return world.Spawn<SalvageMachine>(GD.Load<PackedScene>(ScenePath), PeerId.Server,
            new Godot.Collections.Dictionary { ["spawn_transform"] = transform, ["rewards"] = preview }, machine =>
            {
                if (!machine.Rewards.Select(p => (p.Model, p.Condition)).SequenceEqual(rewards.Select(p => (p.Definition.Model, p.Condition))))
                    throw new InvalidOperationException("Machine reward projection mismatch.");
                machine._rewards = rewards;
            });
    }
    public void ApplyNetworkSpawnData(Variant data)
    {
        if (IsInsideTree() || Rewards.Count != 0) throw new InvalidOperationException("Machine spawn data initializes once before tree entry.");
        var fields = data.AsGodotDictionary();
        Transform3D transform = fields["spawn_transform"].AsTransform3D();
        if (!transform.IsFinite()) throw new ArgumentException("Invalid machine transform.");
        var catalog = GD.Load<MechPartCatalogResource>("res://game/mech/definitions/catalog.tres").Freeze();
        var preview = fields["rewards"].AsGodotArray().Select(v =>
        {
            var p = v.AsGodotDictionary(); string model = p["model"].AsString(); float condition = p["condition"].AsSingle();
            catalog.Resolve(model);
            if (!float.IsFinite(condition) || condition < 0 || condition > 1) throw new ArgumentException("Invalid reward condition.");
            return new SalvageRewardPreview(model, condition);
        }).ToArray();
        if (preview.Length != 2) throw new ArgumentException("Machine requires two prototype rewards.");
        Rewards = Array.AsReadOnly(preview); Transform = transform;
    }
    public override void _Ready()
    {
        if (Rewards.Count != 2 || (Multiplayer.IsServer() && _rewards.Length != 2)) throw new InvalidOperationException("Machine requires initialized rewards.");
        _status = GetNode<Label3D>("Status"); _hud = GetNode<Label>("HackHUD/Challenge");
        _hud.Visible = false;
        _material = new StandardMaterial3D(); GetNode<MeshInstance3D>("Mesh").MaterialOverride = _material;
        _replication = NetworkObject.GetComponent<ReplicationComponent>();
        _replication.Synchronized += Observe; _replication.DeltaSynchronized += Observe;
        Observe();
    }
    public bool CanInteract(InteractionContext context) => State == SalvageMachineState.Neutral &&
        context.Player.IsBound && context.Player.World == NetworkObject.World && context.Player.Host is PlayableMech;
    public void Interact(InteractionContext context)
    {
        RequireServer();
        if (!CanInteract(context) || context.Player.OwnerPeerId != context.RequestingPeerId ||
            context.Target != NetworkObject || context.Player.Host is not Node3D actor ||
            actor.GlobalPosition.DistanceTo(GlobalPosition) > EncounterRange) return;
        OperatorNetworkObjectId = context.Player.Id.Value;
        _challenge = new(Enumerable.Range(0, 4).Select(_ => Random.Shared.Next(1, 5)));
        Sequence = _challenge.Sequence; Progress = 0; InputOpen = false;
        _previewRemaining = PreviewSeconds; _responseRemaining = ResponseSeconds;
        State = SalvageMachineState.Hacking; Publish("started");
    }
    public override void _Process(double delta)
    {
        if (Multiplayer.IsServer()) Advance(delta);
        bool localOperator = State == SalvageMachineState.Hacking && ResolveOperator() is { } actor &&
            actor.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        _hud.Visible = localOperator;
        if (localOperator)
        {
            _hud.Text = InputOpen ? $"SALVAGE: enter 1 / 2 / 3 / 4   {Progress}/4" : $"REMEMBER: {string.Join("  ", Sequence)}";
            for (int symbol = 1; symbol <= 4; symbol++)
                if (Input.IsActionJustPressed($"hack_{symbol}")) RequestHackInput(symbol);
        }
        if (State == SalvageMachineState.SelfDestructing)
            _material.AlbedoColor = (Time.GetTicksMsec() / 150 % 2) == 0 ? Colors.Red : Colors.DarkRed;
    }
    private void Advance(double delta)
    {
        if (State == SalvageMachineState.SelfDestructing)
        {
            _warningRemaining -= delta;
            if (_warningRemaining <= 0 && !IsQueuedForDeletion()) NetworkObject.World.Despawn(NetworkObject.Id);
            return;
        }
        if (State != SalvageMachineState.Hacking) return;
        if (ResolveOperator() is null) { Fail("operator_invalid_or_out_of_range"); return; }
        if (_previewRemaining > 0)
        {
            _previewRemaining = Math.Max(0, _previewRemaining - delta);
            if (_previewRemaining == 0) { InputOpen = true; Publish("input_open"); }
        }
        else
        {
            _responseRemaining -= delta;
            if (_responseRemaining <= 0) Fail("timeout");
        }
    }
    private NetworkObject? ResolveOperator()
    {
        if (OperatorNetworkObjectId <= 0 || !NetworkObject.IsBound ||
            !NetworkObject.World.TryGet(new(OperatorNetworkObjectId), out var actor) || actor is null ||
            !actor.IsBound || actor.World != NetworkObject.World || actor.Host is not PlayableMech body ||
            !body.IsInsideTree() || body.IsQueuedForDeletion() || body.GlobalPosition.DistanceTo(GlobalPosition) > EncounterRange)
            return null;
        return actor;
    }
    public void RequestHackInput(int symbol)
    {
        var actor = ResolveOperator();
        if (actor is null || actor.OwnerPeerId.Value != Multiplayer.GetUniqueId()) return;
        if (Multiplayer.IsServer()) TrySubmit(PeerId.Server, symbol);
        else RpcId(PeerId.Server.Value, MethodName.SubmitHackRpc, symbol);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitHackRpc(int symbol)
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender > 0) TrySubmit(new(sender), symbol);
    }
    internal bool TrySubmit(PeerId sender, int symbol)
    {
        RequireServer();
        if (State != SalvageMachineState.Hacking || symbol is < 1 or > 4) return false;
        var actor = ResolveOperator();
        if (actor is null) { Fail("operator_invalid_or_out_of_range"); return false; }
        if (actor.OwnerPeerId != sender || !InputOpen || _responseRemaining <= 0) return false;
        if (!_challenge!.Submit(symbol)) return false;
        Progress = _challenge.Progress;
        if (_challenge.Result == SalvageChallengeResult.Failure) Fail("wrong_symbol");
        else if (_challenge.Result == SalvageChallengeResult.Success) ReleaseRewards();
        else Publish("progress");
        return true;
    }
    private void ReleaseRewards()
    {
        // Enter terminal state first. No repeated/queued input can release again.
        State = SalvageMachineState.Disabled; StopChallenge(); Publish("disabled");
        var spawned = new List<NetworkObjectId>();
        try
        {
            for (int i = 0; i < _rewards.Length; i++)
            {
                Transform3D transform = GlobalTransform;
                transform.Origin += transform.Basis * new Vector3(i == 0 ? -1.1f : 1.1f, -0.35f, 0.5f);
                var loose = LooseMechPart.Spawn(NetworkObject.World, _rewards[i], transform);
                spawned.Add(loose.NetworkObject.Id);
            }
            _rewards = [];
        }
        catch
        {
            foreach (var id in spawned) NetworkObject.World.Despawn(id);
            _rewards = []; State = SalvageMachineState.SelfDestructing; _warningRemaining = WarningSeconds;
            Publish("reward_spawn_failed"); throw;
        }
    }
    private void Fail(string reason)
    {
        State = SalvageMachineState.SelfDestructing; StopChallenge();
        _rewards = []; _warningRemaining = WarningSeconds; Publish(reason);
    }
    private void StopChallenge()
    {
        _previewRemaining = 0; _responseRemaining = 0; InputOpen = false;
        OperatorNetworkObjectId = 0; Sequence = Array.Empty<int>(); _challenge = null;
    }
    internal IReadOnlyList<PartInstance> GetServerRewards()
    {
        RequireServer(); return Array.AsReadOnly(_rewards);
    }
    private void RequireServer()
    {
        if (!IsInsideTree() || !Multiplayer.IsServer() || !NetworkObject.IsBound || GetMultiplayerAuthority() != 1)
            throw new InvalidOperationException("Only bound server gameplay may mutate salvage state or access rewards.");
    }
    private void Publish(string reason)
    {
        EncounterState = JsonSerializer.Serialize(new Observation(State, OperatorNetworkObjectId, Sequence.ToArray(), Progress, InputOpen));
        Render();
        GameLog.Info("salvage", reason, fields: new Dictionary<string, string?>
        { ["machine"] = NetworkObject.Id.ToString(), ["state"] = State.ToString(), ["operator"] = OperatorNetworkObjectId.ToString(), ["progress"] = Progress.ToString() });
    }
    private void Observe()
    {
        if (!Multiplayer.IsServer())
        {
            var state = JsonSerializer.Deserialize<Observation>(EncounterState) ?? throw new ArgumentException("Missing salvage state.");
            if (!Enum.IsDefined(state.State) || state.OperatorId < 0 || state.Progress is < 0 or > 4 ||
                state.Sequence is null || state.Sequence.Any(s => s is < 1 or > 4) ||
                (state.State == SalvageMachineState.Hacking ? state.OperatorId == 0 || state.Sequence.Length != 4 : state.OperatorId != 0 || state.Sequence.Length != 0 || state.InputOpen))
                throw new ArgumentException("Invalid salvage observation.");
            State = state.State; OperatorNetworkObjectId = state.OperatorId; Sequence = Array.AsReadOnly(state.Sequence);
            Progress = state.Progress; InputOpen = state.InputOpen;
        }
        Render();
    }
    private void Render()
    {
        if (State == SalvageMachineState.Neutral) AddToGroup("interactable"); else RemoveFromGroup("interactable");
        string preview = string.Join("\n", Rewards.Select(p => $"{p.Model.Replace("debug_", "")} {p.Condition:P0}"));
        _status.Text = State switch
        {
            SalvageMachineState.Neutral => $"SALVAGE MACHINE\n{preview}\nE: attempt salvage",
            SalvageMachineState.Hacking => $"HACKING  {Progress}/4\nOperator {OperatorNetworkObjectId}",
            SalvageMachineState.Disabled => "DISABLED\nSalvage released",
            _ => "SELF DESTRUCT"
        };
        _material.AlbedoColor = State switch { SalvageMachineState.Neutral => Colors.Goldenrod, SalvageMachineState.Hacking => Colors.DodgerBlue,
            SalvageMachineState.Disabled => Colors.DarkSlateGray, _ => Colors.Red };
    }
    public override void _ExitTree()
    {
        if (_replication is not null) { _replication.Synchronized -= Observe; _replication.DeltaSynchronized -= Observe; }
        StopChallenge(); _warningRemaining = 0; _rewards = []; CleanedUp = true;
    }
}
