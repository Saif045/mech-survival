using System;

namespace MechSurvival.Mech;

/// <summary>Session runtime part identity; unrelated to account, player or network object IDs.</summary>
public readonly record struct MechPartId(Guid Value);

public sealed class PartInstance
{
    public MechPartId Id { get; } = new(Guid.NewGuid());
    public PartDefinition Definition { get; }
    public float Condition { get; private set; }
    public MechSlot? InstalledSlot { get; internal set; }
    internal MechBody? InstalledBody { get; set; }

    // Only trusted server gameplay constructs runtime parts; clients receive value projections.
    public PartInstance(PartDefinition definition, float condition = 1)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        SetCondition(condition);
    }

    internal void SetCondition(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(nameof(value), "Condition must be finite and in [0, 1].");
        Condition = value;
    }
}
