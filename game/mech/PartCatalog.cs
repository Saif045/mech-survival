using System;
using System.Collections.Generic;

namespace MechSurvival.Mech;

/// <summary>Only the authored mech models; immutable definitions, no runtime items.</summary>
public sealed class PartCatalog
{
    private readonly Dictionary<string, PartDefinition> _models = new(StringComparer.Ordinal);
    public PartCatalog(IEnumerable<PartDefinition> definitions)
    {
        foreach (var definition in definitions)
            if (!_models.TryAdd(definition.Model, definition))
                throw new ArgumentException($"Duplicate mech model: {definition.Model}.");
    }
    public PartDefinition Resolve(string model) => _models.TryGetValue(model, out var definition)
        ? definition : throw new ArgumentException($"Unknown mech model: {model}.");
}

/// <summary>Read-only network observation, never a mutable PartInstance.</summary>
public sealed record LoosePartProjection
{
    public MechPartId Id { get; }
    public string Model { get; }
    public float Condition { get; }
    public LoosePartProjection(MechPartId id, string model, float condition, PartCatalog catalog)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("Empty mech part identity.");
        catalog.Resolve(model);
        if (!float.IsFinite(condition) || condition < 0 || condition > 1)
            throw new ArgumentOutOfRangeException(nameof(condition));
        Id = id; Model = model; Condition = condition;
    }
    public void ValidateInstance(PartInstance part, PartCatalog catalog)
    {
        if (part.Id != Id || part.Definition != catalog.Resolve(Model) || part.Condition != Condition || part.InstalledSlot is not null)
            throw new ArgumentException("Loose embodiment does not match the authoritative uninstalled part.");
    }
}
