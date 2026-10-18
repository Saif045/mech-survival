using System;
using System.Collections.Generic;
using System.Linq;

namespace MechSurvival.Mech;

/// <summary>Damage in normalized condition space. A null ID denotes an empty slot.</summary>
public readonly record struct PartDamageResult(MechPartId? PartId, MechSlot Slot,
    float PreviousCondition, float NewCondition, float RequestedDamage)
{
    public float AppliedDamage => PreviousCondition - NewCondition;
    public bool Changed => NewCondition != PreviousCondition;
    public bool DestroyedByThisOperation => PreviousCondition > 0 && NewCondition == 0;
}

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
        if (!CanAttachPart(slot, part)) return false;
        Install(slot, part);
        return true;
    }

    public bool CanAttachPart(MechSlot slot, PartInstance part) =>
        CanReplacePart(slot, part) && !_parts.ContainsKey(slot);

    /// <summary>Validates empty or occupied targets without changing either part's ownership.</summary>
    public bool CanReplacePart(MechSlot slot, PartInstance part)
    {
        ValidateSlot(slot);
        ArgumentNullException.ThrowIfNull(part);
        return CanInstall(slot, part);
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
        previous = null;
        // Validate before removing anything. Reinstalling the same instance is rejected.
        if (!CanReplacePart(slot, part)) return false;
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

    public PartDamageResult ApplyDamage(MechSlot slot, float amount)
    {
        if (!float.IsFinite(amount) || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Damage must be finite and nonnegative.");
        var part = GetPart(slot);
        if (part is null) return new(null, slot, 0, 0, amount);
        float previous = part.Condition;
        float next = MathF.Max(0, previous - amount);
        if (next != previous) part.SetCondition(next);
        return new(part.Id, slot, previous, next, amount);
    }

    public void Clear()
    {
        foreach (var slot in Slots) DetachPart(slot);
    }

    public MechBodyView Inspect() => new(_parts.OrderBy(p => p.Key).Select(p =>
        new InstalledPart(p.Value.Id, p.Key, p.Value.Definition, p.Value.Condition)));
}
