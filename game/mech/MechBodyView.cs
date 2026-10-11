using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MechSurvival.Mech;

public sealed record InstalledPart(MechPartId Id, MechSlot Slot, PartDefinition Definition, float Condition)
{
    public bool Functioning => Condition > 0;
}

/// <summary>Immutable observation, safe to expose to clients and higher-level systems.</summary>
public sealed class MechBodyView
{
    public IReadOnlyList<MechSlot> Slots => MechBody.Slots;
    public IReadOnlyList<InstalledPart> Parts { get; }
    public MechBodyView(IEnumerable<InstalledPart> parts)
    {
        var copy = parts.OrderBy(p => p.Slot).ToArray();
        if (copy.Any(p => p.Id.Value == Guid.Empty || !Enum.IsDefined(p.Slot) ||
            !p.Definition.Supports(p.Slot) || !float.IsFinite(p.Condition) || p.Condition < 0 || p.Condition > 1) ||
            copy.Select(p => p.Slot).Distinct().Count() != copy.Length ||
            copy.Select(p => p.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("Invalid body snapshot.", nameof(parts));
        Parts = Array.AsReadOnly(copy);
    }

    public InstalledPart? GetPart(MechSlot slot)
    {
        if (!Enum.IsDefined(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        return Parts.FirstOrDefault(p => p.Slot == slot);
    }
    public bool HasPart(MechSlot slot) => GetPart(slot) is not null;
    public int CountCapability(PartCapability capability, bool functioningOnly = true) =>
        Parts.Count(p => (!functioningOnly || p.Functioning) && p.Definition.Capabilities.Contains(capability));

    // Raw installed base contributions: no speculative condition scaling or gameplay effect.
    public float SumContribution(PartStat stat) =>
        Parts.Sum(p => p.Definition.Contributions.GetValueOrDefault(stat));

    public string Serialize() => JsonSerializer.Serialize(Parts.Select(p =>
        new PartState(p.Id.Value, p.Slot, p.Definition.Model, p.Condition)).ToArray());

    public static MechBodyView Deserialize(string json, IReadOnlyDictionary<string, PartDefinition> definitions)
    {
        var states = JsonSerializer.Deserialize<PartState[]>(json)
            ?? throw new ArgumentException("Null body snapshot.", nameof(json));
        return new MechBodyView(states.Select(s => new InstalledPart(new MechPartId(s.Id), s.Slot,
            definitions.TryGetValue(s.Model, out var definition) ? definition :
                throw new ArgumentException($"Unknown part model: {s.Model}"), s.Condition)));
    }

    private sealed record PartState(Guid Id, MechSlot Slot, string Model, float Condition);
}
