using System.Linq;
using System.Collections.Generic;
using Godot;

namespace MechSurvival.Mech;

/// <summary>Inspector authoring resource; frozen into immutable config at body startup.</summary>
[GlobalClass]
public partial class MechPartDefinitionResource : Resource
{
    [Export] public string Model { get; set; } = "";
    // Integer values correspond to MechSlot / PartCapability. Validated on conversion.
    [Export] public int[] CompatibleSlots { get; set; } = [];
    [Export] public int[] Capabilities { get; set; } = [];
    [Export] public float Mobility { get; set; }
    [Export] public float PowerCapacity { get; set; }
    [Export] public float CoolingCapacity { get; set; }

    private PartDefinition? _definition;
    public PartDefinition Freeze() => _definition ??= new(Model, CompatibleSlots.Select(s => (MechSlot)s),
        Capabilities.Select(c => (PartCapability)c), new Dictionary<PartStat, float>
        {
            [PartStat.Mobility] = Mobility,
            [PartStat.PowerCapacity] = PowerCapacity,
            [PartStat.CoolingCapacity] = CoolingCapacity
        });
}
