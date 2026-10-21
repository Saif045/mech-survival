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
    private EnergyHeatSettings _resourceSettings = EnergyHeatSettings.Default;
    private float _externalHeatLoad;
    public float EnvironmentalEnergyDrain { get; private set; }
    public float EnvironmentalHeatLoad { get; private set; }
    public long ParasiteSourceId { get; private set; }
    public float ParasiteEnergyDrain { get; private set; }
    public float ParasiteHeatLoad { get; private set; }
    private double _resourceElapsed;
    private bool _resourceSimulationEnabled = true;
    private bool _energyInitialized;
    public const float ResourceStepSeconds = 0.25f;
    public EnergyHeatSettings ResourceSettings => _resourceSettings;
    public float ExternalHeatLoad => _externalHeatLoad;
    public bool ResourceSimulationEnabled => _resourceSimulationEnabled;
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
        if (!Multiplayer.IsServer()) _resourceSimulationEnabled = false;
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
        _gas!.ReconcileResourceBounds();
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
    public bool InitializeEnergyFull()
    {
        RequireServer();
        if (_energyInitialized) return false;
        var current = ReadSnapshot();
        _gas!.ApplyResources(EnergyHeatPolicy.InitializeFull(new(current.Energy, current.Heat), current.PowerCapacity));
        _energyInitialized = true;
        Publish();
        return true;
    }
    public void SetExternalHeatLoad(float unitsPerSecond)
    {
        RequireServer(); EnergyHeatPolicy.Nonnegative(unitsPerSecond, nameof(unitsPerSecond));
        _externalHeatLoad = unitsPerSecond;
    }
    // Concrete server recovery input; current resources still live in the native ASC.
    internal void RecoverSafeEnergy(float delta)
    {
        RequireServer(); EnergyHeatPolicy.Nonnegative(delta, nameof(delta));
        var current = ReadSnapshot();
        _gas!.ApplyResources(new(MechSurvival.Environment.SafeRecoveryPolicy.RecoveredEnergy(current.Energy, current.PowerCapacity, delta), current.Heat));
        Publish();
    }
    public void SetEnvironmentalLoads(float energyDrain, float heatLoad)
    {
        RequireServer();
        EnergyHeatPolicy.Nonnegative(energyDrain, nameof(energyDrain));
        EnergyHeatPolicy.Nonnegative(heatLoad, nameof(heatLoad));
        EnvironmentalEnergyDrain = energyDrain; EnvironmentalHeatLoad = heatLoad;
    }
    // One concrete parasite contribution; source identity protects against stale cleanup.
    internal bool TrySetParasiteLoad(NetworkObjectId source, float energyDrain, float heatLoad)
    {
        RequireServer();
        EnergyHeatPolicy.Nonnegative(energyDrain, nameof(energyDrain)); EnergyHeatPolicy.Nonnegative(heatLoad, nameof(heatLoad));
        if (source.Value <= 0) throw new ArgumentOutOfRangeException(nameof(source));
        if (ParasiteSourceId != 0) return false;
        ParasiteSourceId = source.Value; ParasiteEnergyDrain = energyDrain; ParasiteHeatLoad = heatLoad;
        return true;
    }
    internal void ClearParasiteLoad(NetworkObjectId source)
    {
        RequireServer();
        if (ParasiteSourceId != source.Value) return;
        ParasiteSourceId = 0; ParasiteEnergyDrain = 0; ParasiteHeatLoad = 0;
    }
    public void ConfigureResources(EnergyHeatSettings settings)
    {
        RequireServer(); settings.Validate(); _resourceSettings = settings;
    }
    public void SetResourceSimulationEnabled(bool enabled)
    {
        RequireServer(); _resourceSimulationEnabled = enabled; _resourceElapsed = 0;
    }
    public EnergyHeatStepResult AdvanceResources(float delta)
    {
        RequireServer();
        var current = ReadSnapshot();
        var previous = new EnergyHeatState(current.Energy, current.Heat);
        var result = EnergyHeatPolicy.Step(previous, current.PowerCapacity, current.CoolingCapacity,
            _externalHeatLoad, delta, _resourceSettings, ParasiteEnergyDrain, ParasiteHeatLoad, EnvironmentalEnergyDrain, EnvironmentalHeatLoad);
        if (result.State != previous) { _gas!.ApplyResources(result.State); Publish(); }
        return result;
    }
    public void ResetResourceState()
    {
        RequireServer(); _externalHeatLoad = 0; _resourceElapsed = 0;
        EnvironmentalEnergyDrain = 0; EnvironmentalHeatLoad = 0;
        ParasiteSourceId = 0; ParasiteEnergyDrain = 0; ParasiteHeatLoad = 0;
        _gas!.ApplyResources(default); Publish();
        // Initialization stays consumed: reset is not a gameplay recharge path.
    }
    internal void SetResourcesForProbe(EnergyHeatState state)
    {
        RequireServer(); _gas!.ApplyResources(state); Publish();
    }
    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer()) return;
        if (_gas!.ConsumeChanged()) Publish();
        if (!_resourceSimulationEnabled) return;
        _resourceElapsed = Math.Min(_resourceElapsed + delta, ResourceStepSeconds * 4);
        int steps = 0;
        while (_resourceElapsed >= ResourceStepSeconds && steps++ < 4)
        {
            _resourceElapsed -= ResourceStepSeconds;
            AdvanceResources(ResourceStepSeconds);
        }
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
        EnvironmentalEnergyDrain = 0; EnvironmentalHeatLoad = 0;
        _gas = null;
        Snapshot = default; _baseline = null; _lastMirror = null;
        _externalHeatLoad = 0; _resourceElapsed = 0; _resourceSimulationEnabled = false; _energyInitialized = false;
        ParasiteSourceId = 0; ParasiteEnergyDrain = 0; ParasiteHeatLoad = 0;
    }
}
