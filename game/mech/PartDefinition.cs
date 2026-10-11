using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MechSurvival.Mech;

/// <summary>Immutable shared configuration. Condition never lives here.</summary>
public sealed class PartDefinition
{
    public string Model { get; }
    public IReadOnlyList<MechSlot> CompatibleSlots { get; }
    public IReadOnlyList<PartCapability> Capabilities { get; }
    public IReadOnlyDictionary<PartStat, float> Contributions { get; }

    public PartDefinition(string model, IEnumerable<MechSlot> slots,
        IEnumerable<PartCapability>? capabilities = null,
        IReadOnlyDictionary<PartStat, float>? contributions = null)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A model key is required.", nameof(model));
        var compatible = slots.Distinct().OrderBy(s => s).ToArray();
        if (compatible.Length == 0 || compatible.Any(s => !Enum.IsDefined(s)))
            throw new ArgumentException("At least one valid compatible slot is required.", nameof(slots));
        var caps = (capabilities ?? []).Distinct().OrderBy(c => c).ToArray();
        if (caps.Any(c => !Enum.IsDefined(c))) throw new ArgumentException("Unknown capability.");
        var stats = new Dictionary<PartStat, float>(contributions ?? new Dictionary<PartStat, float>());
        if (stats.Any(s => !Enum.IsDefined(s.Key) || !float.IsFinite(s.Value) || s.Value < 0))
            throw new ArgumentException("Contributions must be finite and nonnegative.");
        Model = model;
        CompatibleSlots = Array.AsReadOnly(compatible);
        Capabilities = Array.AsReadOnly(caps);
        Contributions = new ReadOnlyDictionary<PartStat, float>(stats);
    }

    public bool Supports(MechSlot slot) => CompatibleSlots.Contains(slot);
}
