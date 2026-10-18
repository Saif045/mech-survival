using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using MechSurvival.Mech.Gas;

namespace MechSurvival.Mech;

/// <summary>A server-owned body, independent of any connected player.</summary>
public partial class NetworkMechBody : CharacterBody3D
{
    [Export] public Godot.Collections.Array<MechPartDefinitionResource> Definitions { get; set; } = [];
    [Export, Replicated(ReplicationMode.OnChange)]
    public string Composition { get; private set; } = "[]";
    [Export, Replicated(ReplicationMode.OnChange)]
    public string GasState { get; private set; } = default(MechGasSnapshot).Serialize();
    public MechGasComponent Gas => GetNode<MechGasComponent>("MechGas");

    internal void PublishGasSnapshot(MechGasSnapshot snapshot)
    {
        RequireServer();
        GasState = snapshot.Serialize();
    }

    private readonly MechBody _body = new();
    private readonly Dictionary<string, PartDefinition> _definitions = new(StringComparer.Ordinal);
    private ReplicationComponent? _replication;
    public MechBodyView View { get; private set; } = new([]);
    public event Action? Changed;

    public override void _Ready()
    {
        foreach (var resource in Definitions)
        {
            var definition = resource.Freeze();
            _definitions.Add(definition.Model, definition);
        }
        var network = GetNode<NetworkObject>("NetworkObject");
        _replication = network.GetComponent<ReplicationComponent>();
        _replication.Synchronized += Observe;
        _replication.DeltaSynchronized += Observe;
        Observe();
    }

    private void RequireServer()
    {
        if (!IsInsideTree() || !Multiplayer.IsServer() || GetMultiplayerAuthority() != 1 ||
            !GetNode<NetworkObject>("NetworkObject").IsBound)
            throw new InvalidOperationException("Only the authoritative server may change mech state.");
    }

    public PartInstance CreatePart(string model, float condition = 1)
    {
        RequireServer();
        return new PartInstance(_definitions[model], condition);
    }

    private void ValidateDefinition(PartInstance part)
    {
        if (!MatchesDefinition(part))
            throw new ArgumentException("Part must use this body's configured definitions.", nameof(part));
    }

    private bool MatchesDefinition(PartInstance part) =>
        _definitions.TryGetValue(part.Definition.Model, out var definition) &&
        definition.CompatibleSlots.SequenceEqual(part.Definition.CompatibleSlots) &&
        definition.Capabilities.SequenceEqual(part.Definition.Capabilities) &&
        definition.Contributions.OrderBy(p => p.Key).SequenceEqual(part.Definition.Contributions.OrderBy(p => p.Key));

    public bool CanReplacePart(MechSlot slot, PartInstance part)
    {
        RequireServer();
        return MatchesDefinition(part) && _body.CanReplacePart(slot, part);
    }

    // Mutable references never become client views. Service uses this to verify recovery/ownership.
    internal PartInstance? GetServerPart(MechSlot slot)
    {
        RequireServer();
        return _body.GetPart(slot);
    }

    public bool AttachPart(MechSlot slot, PartInstance part)
    {
        RequireServer();
        ValidateDefinition(part);
        if (!_body.AttachPart(slot, part)) return false;
        Publish();
        return true;
    }
    public bool ReplacePart(MechSlot slot, PartInstance part, out PartInstance? previous)
    {
        RequireServer();
        ValidateDefinition(part);
        if (!_body.ReplacePart(slot, part, out previous)) return false;
        Publish();
        return true;
    }
    public PartInstance? DetachPart(MechSlot slot)
    {
        RequireServer();
        var previous = _body.DetachPart(slot);
        if (previous is not null) Publish();
        return previous;
    }
    public bool SetCondition(MechSlot slot, float condition)
    {
        RequireServer();
        if (!_body.SetCondition(slot, condition)) return false;
        Publish();
        return true;
    }
    public void ResetBody()
    {
        RequireServer();
        _body.Clear();
        Publish();
    }
    public PartDamageResult ApplyDamage(MechSlot slot, float amount)
    {
        RequireServer();
        var result = _body.ApplyDamage(slot, amount);
        if (result.Changed) Publish();
        return result;
    }
    public InstalledPart? GetPart(MechSlot slot) => View.GetPart(slot);
    public bool HasPart(MechSlot slot) => View.HasPart(slot);

    private void Publish()
    {
        View = _body.Inspect();
        Composition = View.Serialize();
        Changed?.Invoke();
    }
    private void Observe()
    {
        // Parse and validate a complete replacement before publishing the client view.
        View = MechBodyView.Deserialize(Composition, _definitions);
        Changed?.Invoke();
    }
    public override void _ExitTree()
    {
        if (_replication is not null)
        {
            _replication.Synchronized -= Observe;
            _replication.DeltaSynchronized -= Observe;
        }
        _body.Clear();
        View = new([]);
        Composition = "[]";
        GasState = default(MechGasSnapshot).Serialize();
    }
}
