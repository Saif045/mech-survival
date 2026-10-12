using System;
using System.Text.Json;
using MechSurvival.Mech;

namespace MechSurvival.Mech.Gas;

/// <summary>Baseline input or captured canonical GAS output; never part condition.</summary>
public readonly record struct MechGasSnapshot
{
    public float Mobility { get; }
    public float PowerCapacity { get; }
    public float CoolingCapacity { get; }
    public bool HasMobility { get; }
    public bool HasSensor { get; }
    public bool HasPower { get; }
    public bool HasCooling { get; }
    public float Energy { get; }
    public float Heat { get; }
    public bool PowerDepleted { get; }
    public bool ThermalCritical { get; }
    public static MechGasSnapshot ColdEmpty => new(0, 0, 0, false, false, false, false, 0, 0, true, false);

    [System.Text.Json.Serialization.JsonConstructor]
    public MechGasSnapshot(float mobility, float powerCapacity, float coolingCapacity,
        bool hasMobility, bool hasSensor, bool hasPower, bool hasCooling,
        float energy = 0, float heat = 0, bool powerDepleted = false, bool thermalCritical = false)
    {
        foreach (float value in new[] { mobility, powerCapacity, coolingCapacity })
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Mobility = mobility; PowerCapacity = powerCapacity; CoolingCapacity = coolingCapacity;
        HasMobility = hasMobility; HasSensor = hasSensor; HasPower = hasPower; HasCooling = hasCooling;
        _ = new EnergyHeatState(energy, heat);
        if (energy > powerCapacity) throw new ArgumentOutOfRangeException(nameof(energy));
        Energy = energy; Heat = heat; PowerDepleted = powerDepleted; ThermalCritical = thermalCritical;
    }

    public static MechGasSnapshot FromStructure(MechBodyView view) => new(
        view.SumEffectiveContribution(PartStat.Mobility),
        view.SumEffectiveContribution(PartStat.PowerCapacity),
        view.SumEffectiveContribution(PartStat.CoolingCapacity),
        view.CountCapability(PartCapability.Mobility) > 0, view.CountCapability(PartCapability.Sensor) > 0,
        view.CountCapability(PartCapability.Power) > 0, view.CountCapability(PartCapability.Cooling) > 0);

    public string Serialize() => JsonSerializer.Serialize(this);
    public static MechGasSnapshot Deserialize(string json) => JsonSerializer.Deserialize<MechGasSnapshot>(json);
}
