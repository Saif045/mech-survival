using System;
using System.Collections.Generic;
using System.Linq;

namespace MechSurvival.Mech;

/// <summary>Engine-independent rules, called only by authoritative gameplay at runtime.</summary>
public sealed class MechBody
{
    public static IReadOnlyList<MechSlot> Slots { get; } = Array.AsReadOnly(Enum.GetValues<MechSlot>());
    private readonly Dictionary<MechSlot, PartInstance> _parts = new();

    private static void ValidateSlot(MechSlot slot)
    {
        if (!Enum.IsDefined(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    public PartInstance? GetPart(MechSlot slot)
    {
        ValidateSlot(slot);
        return _parts.GetValueOrDefault(slot);
    }

    public bool HasPart(MechSlot slot) => GetPart(slot) is not null;

    public bool AttachPart(MechSlot slot, PartInstance part)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(part);
        if (_parts.ContainsKey(slot) || !CanInstall(slot, part)) return false;
        Install(slot, part);
        return true;
    }

    private bool CanInstall(MechSlot slot, PartInstance part) =>
        part.Definition.Supports(slot) && part.InstalledBody is null &&
        _parts.Values.All(p => p.Id != part.Id);

    private void Install(MechSlot slot, PartInstance part)
    {
        _parts.Add(slot, part);
        part.InstalledBody = this;
        part.InstalledSlot = slot;
    }

    public bool ReplacePart(MechSlot slot, PartInstance part, out PartInstance? previous)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(part);
        previous = null;
        // Validate before removing anything. Reinstalling the same instance is rejected.
        if (!CanInstall(slot, part)) return false;
        previous = DetachPart(slot);
        Install(slot, part);
        return true;
    }

    public PartInstance? DetachPart(MechSlot slot)
    {
        ValidateSlot(slot);
        if (!_parts.Remove(slot, out var previous)) return null;
        previous.InstalledBody = null;
        previous.InstalledSlot = null;
        return previous;
    }

    public bool SetCondition(MechSlot slot, float condition)
    {
        var part = GetPart(slot);
        // Invalid values are rejected even when the target slot is empty.
        if (!float.IsFinite(condition) || condition < 0 || condition > 1)
            throw new ArgumentOutOfRangeException(nameof(condition));
        if (part is null) return false;
        part.SetCondition(condition);
        return true;
    }

    public void Clear()
    {
        foreach (var slot in Slots) DetachPart(slot);
    }

    public MechBodyView Inspect() => new(_parts.OrderBy(p => p.Key).Select(p =>
        new InstalledPart(p.Value.Id, p.Key, p.Value.Definition, p.Value.Condition)));
}
