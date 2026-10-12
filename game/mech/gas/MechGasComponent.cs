using System;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;

namespace MechSurvival.Mech.Gas;

public partial class MechGasComponent : Node
{
    private NetworkMechBody _body = null!;
    private MechGasAdapter? _gas;
    private ReplicationComponent? _replication;
    private MechGasSnapshot? _baseline;
    private MechGasSnapshot? _lastMirror;
    public MechGasSnapshot Snapshot { get; private set; }
    public bool CleanedUp { get; private set; }
    public bool UsesRealAsc => _gas?.UsesRealAsc ?? false;
    public bool HasDevelopmentModifier => _gas?.HasDevelopmentModifier ?? false;
    public event Action? Changed;

    public override void _Ready()
    {
        _body = GetParent<NetworkMechBody>();
        _replication = _body.GetNode<NetworkObject>("NetworkObject").GetComponent<ReplicationComponent>();
        _gas = MechGasAdapter.Create(this, Multiplayer.IsServer());
        _body.Changed += OnStructureChanged;
        _replication.Synchronized += OnReplicated;
        _replication.DeltaSynchronized += OnReplicated;
        if (Multiplayer.IsServer()) ReconcileStructure(); else OnReplicated();
    }
    private void RequireServer()
    {
        if (!IsInsideTree() || !Multiplayer.IsServer() || _body.GetMultiplayerAuthority() != 1)
            throw new InvalidOperationException("Only the authoritative server may mutate mech GAS.");
    }
    private void OnStructureChanged()
    {
        if (Multiplayer.IsServer()) ReconcileStructure();
    }
    internal void ReconcileStructure()
    {
        RequireServer();
        var baseline = MechGasSnapshot.FromStructure(_body.View);
        if (_baseline == baseline) return;
        _gas!.UpdateStructure(baseline);
        _baseline = baseline;
        Publish();
    }
    private void Publish()
    {
        var snapshot = _gas!.Capture();
        _gas.ConsumeChanged();
        if (snapshot == Snapshot && _body.GasState == snapshot.Serialize()) return;
        Snapshot = snapshot;
        _body.PublishGasSnapshot(snapshot);
        Changed?.Invoke();
    }
    private void OnReplicated()
    {
        if (Multiplayer.IsServer()) return;
        var snapshot = MechGasSnapshot.Deserialize(_body.GasState);
        if (_lastMirror == snapshot) return;
        _gas!.ApplyMirror(snapshot);
        Snapshot = _gas.Capture();
        if (Snapshot != snapshot) throw new InvalidOperationException("Local ASC mirror differs from canonical GAS state.");
        _lastMirror = snapshot;
        Changed?.Invoke();
    }
    internal bool SetDevelopmentModifier(bool enabled)
    {
        RequireServer();
        bool changed = _gas!.SetDevelopmentModifier(enabled);
        if (changed) Publish();
        return changed;
    }
    public MechGasSnapshot ReadSnapshot() => _gas?.Capture() ?? default;
    public override void _Process(double delta)
    {
        if (Multiplayer.IsServer() && _gas!.ConsumeChanged()) Publish();
    }
    public override void _ExitTree()
    {
        if (_body is not null) _body.Changed -= OnStructureChanged;
        if (_replication is not null)
        {
            _replication.Synchronized -= OnReplicated;
            _replication.DeltaSynchronized -= OnReplicated;
        }
        CleanedUp = _gas?.Shutdown() ?? true;
        if (!CleanedUp) GD.PushError("Mech GAS teardown retained attributes, effects or tags.");
        _gas = null;
        Snapshot = default; _baseline = null; _lastMirror = null;
    }
}
