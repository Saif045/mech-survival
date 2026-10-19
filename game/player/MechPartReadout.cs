using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MechSurvival.Mech;

namespace MechSurvival.Player;

/// <summary>Formatting immutable physical observations only; never predicts GAS.</summary>
public static class MechPartReadout
{
    public static string Name(string model) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(model.Replace('_', ' '));
    public static string SlotName(MechSlot slot) => slot.ToString().Replace("Left", "Left ").Replace("Right", "Right ");
    public static string StatName(PartStat stat) => stat switch { PartStat.PowerCapacity => "Power Capacity", PartStat.CoolingCapacity => "Cooling Capacity", _ => "Mobility" };
    public static string Describe(PartDefinition definition, float condition) => definition.Contributions.Count == 0 && definition.Capabilities.Count == 0 ? "Structural module" : string.Join(" / ",
        definition.Contributions.Select(p => $"{StatName(p.Key)} {p.Value * condition:F2}")
            .Concat(definition.Capabilities.Select(c => $"{c}: {(condition > 0 ? "available" : "offline")}")));
    public static string Installed(MechBodyView view, MechSlot slot) => view.GetPart(slot) is { } part
        ? $"{Name(part.Definition.Model)}   Condition {part.Condition:P0}\n{Describe(part.Definition, part.Condition)}"
        : "EMPTY";
    public static float Difference(InstalledPart? current, PartDefinition carried, float condition, PartStat stat) =>
        carried.Contributions.GetValueOrDefault(stat) * condition - (current?.GetEffectiveContribution(stat) ?? 0);
    public static string Change(InstalledPart? current, PartDefinition carried, float condition)
    {
        var changes = Enum.GetValues<PartStat>().Where(stat => carried.Contributions.ContainsKey(stat) || current?.Definition.Contributions.ContainsKey(stat) == true)
            .Select(stat => $"{StatName(stat)} {Difference(current, carried, condition, stat):+0.00;-0.00;0.00}").ToArray();
        return changes.Length == 0 ? "No physical contribution change" : string.Join(" / ", changes);
    }
}
