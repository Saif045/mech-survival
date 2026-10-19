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
using MechSurvival.Mech.Gas;
using MechSurvival.Player;

namespace MechSurvival.Parasites;

/// <summary>The concrete lurking/attached threat; it never changes parent or authority.</summary>
public partial class Parasite : Node3D, INetworkSpawnInitializable, IInteractable
{
    public const string ScenePath = "res://game/parasite/parasite.tscn";
    public const float TriggerRadius = 1.6f, PurgeRange = 2.75f, EnergyDrain = 0.2f, HeatLoad = 6;
    public const double CursorStepSeconds = 0.35, AttemptSeconds = 12;
    public sealed record Observation(ParasiteState State, long AttachedId, long OperatorId, int Cursor,
        int SafeStart, int SafeEnd, int RequiredHits, int Progress, bool Armed, bool Teammate);
    private static string EmptyState => JsonSerializer.Serialize(new Observation(ParasiteState.Lurking, 0, 0, 0, 0, 0, 0, 0, false, false));
    [Export, Replicated(ReplicationMode.OnChange)] public string InfectionState { get; private set; } = EmptyState;
    public ParasiteState State { get; private set; }
    public long AttachedNetworkObjectId { get; private set; }
    public long OperatorNetworkObjectId { get; private set; }
    public int Cursor { get; private set; }
    public int SafeStart { get; private set; }
    public int SafeEnd { get; private set; }
    public int RequiredHits { get; private set; }
    public int Progress { get; private set; }
    public bool Armed { get; private set; }
    public bool Teammate { get; private set; }
    public bool CleanedUp { get; private set; }
    public bool HasActiveTimer => _remaining > 0 || _cursorElapsed > 0;
    public NetworkObject NetworkObject => GetNode<NetworkObject>("NetworkObject");
    private PurgeChallenge? _challenge;
    private MechGasComponent? _loadGas;
    private NetworkObjectId _identity;
    private double _remaining, _cursorElapsed;
    private bool _initialized;
    private ReplicationComponent? _replication;
    private Label3D _status = null!;
    private Label _hud = null!;
    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;

    public static Parasite Spawn(NetworkWorld world, Transform3D transform)
    {
        if (!world.Multiplayer.IsServer()) throw new InvalidOperationException("Only the server spawns parasites.");
        return world.Spawn<Parasite>(GD.Load<PackedScene>(ScenePath), PeerId.Server,
            new Godot.Collections.Dictionary { ["spawn_transform"] = transform });
    }
    public void ApplyNetworkSpawnData(Variant data)
    {
        if (_initialized || IsInsideTree()) throw new InvalidOperationException("Parasite spawn initializes once before entry.");
        var transform = data.AsGodotDictionary()["spawn_transform"].AsTransform3D();
        if (!transform.IsFinite()) throw new ArgumentException("Invalid parasite transform.");
        Transform = transform; _initialized = true;
    }
    public override void _Ready()
    {
        if (!_initialized) throw new InvalidOperationException("Parasite requires spawn data.");
        _identity = NetworkObject.Id;
        _status = GetNode<Label3D>("Status"); _hud = GetNode<Label>("PurgeHUD/Timing"); _hud.Visible = false;
        _mesh = GetNode<MeshInstance3D>("Mesh");
        _material = new StandardMaterial3D { EmissionEnabled = true }; _mesh.MaterialOverride = _material;
        _replication = NetworkObject.GetComponent<ReplicationComponent>();
        _replication.Synchronized += Observe; _replication.DeltaSynchronized += Observe;
        Observe();
    }
    public override void _Process(double delta)
    {
        if (Multiplayer.IsServer()) Advance(delta);
        var target = Target();
        if (target?.Host is PlayableMech body)
            GlobalTransform = body.GetNode<Node3D>("Presentation/ParasiteAnchor").GlobalTransform;
        bool localOperator = State == ParasiteState.Purging && Operator() is { } actor && actor.OwnerPeerId.Value == Multiplayer.GetUniqueId();
        _hud.Visible = localOperator;
        if (localOperator)
        {
            string sweep = string.Join(" ", Enumerable.Range(0, 8).Select(i => i == Cursor ? "[^]" : $"[{i}]"));
            _hud.Text = $"{(Teammate ? "TEAMMATE" : "SELF")} PURGE  {Progress}/{RequiredHits}\n{sweep}\nSafe: {SafeStart}-{SafeEnd}   T: pulse";
            if (Input.IsActionJustPressed("purge_pulse")) RequestPulse();
        }
        string resources = target?.Host is PlayableMech infected ? $"\nEnergy {infected.Gas.Snapshot.Energy:F1}  Heat {infected.Gas.Snapshot.Heat:F1}" : "";
        _status.Text = State switch
        {
            ParasiteState.Lurking => "PARASITE\nDanger: proximity attachment",
            ParasiteState.Attached => $"PARASITE ATTACHED to {AttachedNetworkObjectId}{resources}\nE: purge (self or teammate)",
            ParasiteState.Purging => $"PURGING mech {AttachedNetworkObjectId}\nOperator {OperatorNetworkObjectId}  {Progress}/{RequiredHits}{resources}",
            _ => "EXPELLED"
        };
        float pulse = 1 + 0.08f * MathF.Sin(Time.GetTicksMsec() * 0.008f);
        _mesh.Scale = Vector3.One * pulse;
    }
    private void Advance(double delta)
    {
        if (IsQueuedForDeletion()) return;
        if (State == ParasiteState.Lurking)
        {
            foreach (var actor in NetworkObject.World.Objects.OrderBy(o => o.Id.Value))
            {
                if (actor.Host is not PlayableMech body || body.IsQueuedForDeletion() || !actor.IsBound ||
                    body.GlobalPosition.DistanceTo(GlobalPosition) > TriggerRadius || body.Gas.ParasiteSourceId != 0) continue;
                if (!body.Gas.TrySetParasiteLoad(_identity, EnergyDrain, HeatLoad)) continue;
                _loadGas = body.Gas; AttachedNetworkObjectId = actor.Id.Value;
                State = ParasiteState.Attached; Publish("attached"); break;
            }
            return;
        }
        if (State == ParasiteState.Expelled) return;
        if (Target() is null) { Expel("target_removed"); return; }
        if (State != ParasiteState.Purging) return;
        if (Operator() is null) { Abort("operator_invalid_or_range"); return; }
        _remaining -= delta;
        if (_remaining <= 0) { Abort("timeout"); return; }
        _cursorElapsed += delta;
        bool changed = false;
        while (_cursorElapsed >= CursorStepSeconds)
        {
            _cursorElapsed -= CursorStepSeconds; _challenge!.Advance(); changed = true;
        }
        if (changed) { CopyChallenge(); Publish("cursor"); }
    }
    private NetworkObject? Target()
    {
        if (AttachedNetworkObjectId <= 0 || !NetworkObject.IsBound ||
            !NetworkObject.World.TryGet(new(AttachedNetworkObjectId), out var actor) || actor is null ||
            !actor.IsBound || actor.World != NetworkObject.World || actor.Host is not PlayableMech body ||
            !body.IsInsideTree() || body.IsQueuedForDeletion() ||
            (Multiplayer.IsServer() && body.Gas.ParasiteSourceId != _identity.Value)) return null;
        return actor;
    }
    private NetworkObject? Operator()
    {
        var target = Target();
        if (target?.Host is not PlayableMech infected || OperatorNetworkObjectId <= 0 ||
            !NetworkObject.World.TryGet(new(OperatorNetworkObjectId), out var actor) || actor is null ||
            !actor.IsBound || actor.World != NetworkObject.World || actor.Host is not PlayableMech body ||
            !body.IsInsideTree() || body.IsQueuedForDeletion() || body.GlobalPosition.DistanceTo(infected.GlobalPosition) > PurgeRange) return null;
        return actor;
    }
    public bool CanInteract(InteractionContext context) => State == ParasiteState.Attached && Target() is not null &&
        context.Player.IsBound && context.Player.World == NetworkObject.World && context.Player.Host is PlayableMech;
    public void Interact(InteractionContext context)
    {
        RequireServer();
        if (!CanInteract(context) || context.Target != NetworkObject || context.Player.OwnerPeerId != context.RequestingPeerId) return;
        var target = (PlayableMech)Target()!.Host;
        if (((PlayableMech)context.Player.Host).GlobalPosition.DistanceTo(target.GlobalPosition) > PurgeRange) return;
        OperatorNetworkObjectId = context.Player.Id.Value; Teammate = OperatorNetworkObjectId != AttachedNetworkObjectId;
        _challenge = Teammate ? PurgeChallenge.Teammate() : PurgeChallenge.Self();
        _remaining = AttemptSeconds; _cursorElapsed = 0; CopyChallenge();
        State = ParasiteState.Purging; Publish("purge_started");
    }
    public void RequestPulse()
    {
        var actor = Operator();
        if (actor is null || actor.OwnerPeerId.Value != Multiplayer.GetUniqueId()) return;
        if (Multiplayer.IsServer()) TryPulse(PeerId.Server);
        else RpcId(PeerId.Server.Value, MethodName.PulseRpc);
    }
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PulseRpc()
    {
        if (!Multiplayer.IsServer()) return;
        long sender = Multiplayer.GetRemoteSenderId();
        if (sender > 0) TryPulse(new(sender));
    }
    internal bool TryPulse(PeerId sender)
    {
        RequireServer();
        if (State != ParasiteState.Purging) return false;
        if (Target() is null) { Expel("target_removed"); return false; }
        var actor = Operator();
        if (actor is null || _remaining <= 0) { Abort("operator_invalid_or_timeout"); return false; }
        if (actor.OwnerPeerId != sender || !_challenge!.Armed) return false;
        bool hit = _challenge.Pulse(); CopyChallenge();
        if (_challenge.Succeeded) Expel("purged"); else Publish(hit ? "hit" : "miss");
        return hit;
    }
    private void CopyChallenge()
    {
        Cursor = _challenge!.Cursor; SafeStart = _challenge.SafeStart; SafeEnd = _challenge.SafeEnd;
        RequiredHits = _challenge.RequiredHits; Progress = _challenge.Progress; Armed = _challenge.Armed;
    }
    private void StopChallenge()
    {
        _challenge = null; _remaining = 0; _cursorElapsed = 0; OperatorNetworkObjectId = 0;
        Cursor = 0; SafeStart = 0; SafeEnd = 0; RequiredHits = 0; Progress = 0; Armed = false; Teammate = false;
    }
    private void Abort(string reason) { StopChallenge(); State = ParasiteState.Attached; Publish(reason); }
    private void ClearLoad()
    {
        if (_loadGas is not null && GodotObject.IsInstanceValid(_loadGas) && _loadGas.IsInsideTree() && Multiplayer.IsServer())
            _loadGas.ClearParasiteLoad(_identity);
        _loadGas = null;
    }
    private void Expel(string reason)
    {
        ClearLoad(); StopChallenge(); AttachedNetworkObjectId = 0; State = ParasiteState.Expelled;
        Publish(reason); NetworkObject.World.Despawn(_identity);
    }
    private void RequireServer()
    {
        if (!IsInsideTree() || !Multiplayer.IsServer() || !NetworkObject.IsBound || GetMultiplayerAuthority() != 1)
            throw new InvalidOperationException("Only bound server gameplay may mutate parasite state.");
    }
    private void Publish(string reason)
    {
        InfectionState = JsonSerializer.Serialize(new Observation(State, AttachedNetworkObjectId, OperatorNetworkObjectId, Cursor,
            SafeStart, SafeEnd, RequiredHits, Progress, Armed, Teammate));
        Render();
        if (reason != "cursor") GameLog.Info("parasite", reason, fields: new Dictionary<string, string?>
        { ["parasite"] = _identity.ToString(), ["attached"] = AttachedNetworkObjectId.ToString(), ["operator"] = OperatorNetworkObjectId.ToString(), ["progress"] = Progress.ToString() });
    }
    private void Observe()
    {
        if (!Multiplayer.IsServer())
        {
            var state = JsonSerializer.Deserialize<Observation>(InfectionState) ?? throw new ArgumentException("Missing parasite observation.");
            if (!Enum.IsDefined(state.State) || state.AttachedId < 0 || state.OperatorId < 0 || state.Cursor is < 0 or > 7 || state.Progress < 0 ||
                (state.State == ParasiteState.Purging ? state.AttachedId == 0 || state.OperatorId == 0 ||
                    state.SafeStart != (state.Teammate ? 2 : 3) || state.SafeEnd != (state.Teammate ? 5 : 4) ||
                    state.RequiredHits != (state.Teammate ? 2 : 3) || state.Progress >= state.RequiredHits :
                    state.OperatorId != 0 || state.RequiredHits != 0 || state.Progress != 0 || state.Armed || state.Teammate) ||
                (state.State == ParasiteState.Attached && state.AttachedId == 0) ||
                ((state.State == ParasiteState.Lurking || state.State == ParasiteState.Expelled) && state.AttachedId != 0))
                throw new ArgumentException("Invalid parasite observation.");
            State = state.State; AttachedNetworkObjectId = state.AttachedId; OperatorNetworkObjectId = state.OperatorId;
            Cursor = state.Cursor; SafeStart = state.SafeStart; SafeEnd = state.SafeEnd; RequiredHits = state.RequiredHits;
            Progress = state.Progress; Armed = state.Armed; Teammate = state.Teammate;
        }
        Render();
    }
    private void Render()
    {
        if (State == ParasiteState.Attached) AddToGroup("interactable"); else RemoveFromGroup("interactable");
        _material.AlbedoColor = State == ParasiteState.Lurking ? Colors.Magenta : Colors.OrangeRed;
        _material.Emission = _material.AlbedoColor;
    }
    public override void _ExitTree()
    {
        ClearLoad(); StopChallenge(); AttachedNetworkObjectId = 0;
        if (_replication is not null) { _replication.Synchronized -= Observe; _replication.DeltaSynchronized -= Observe; }
        CleanedUp = true;
    }
}
