using System;

namespace MechSurvival.Mech.Gas;

public readonly record struct EnergyHeatState
{
    public float Energy { get; }
    public float Heat { get; }
    public EnergyHeatState(float energy, float heat)
    {
        EnergyHeatPolicy.Nonnegative(energy, nameof(energy));
        EnergyHeatPolicy.Nonnegative(heat, nameof(heat));
        if (heat > 100) throw new ArgumentOutOfRangeException(nameof(heat));
        Energy = energy; Heat = heat;
    }
}

public readonly record struct EnergyHeatSettings
{
    public static EnergyHeatSettings Default => new(0.1f, 1, 0.25f);
    public float BaseEnergyDrainPerSecond { get; }
    public float CoolingRateScale { get; }
    public float CoolingEnergyCostPerHeat { get; }
    public EnergyHeatSettings(float baseEnergyDrainPerSecond, float coolingRateScale, float coolingEnergyCostPerHeat)
    {
        BaseEnergyDrainPerSecond = baseEnergyDrainPerSecond;
        CoolingRateScale = coolingRateScale;
        CoolingEnergyCostPerHeat = coolingEnergyCostPerHeat;
        Validate();
    }
    internal void Validate()
    {
        EnergyHeatPolicy.Nonnegative(BaseEnergyDrainPerSecond, nameof(BaseEnergyDrainPerSecond));
        EnergyHeatPolicy.Nonnegative(CoolingRateScale, nameof(CoolingRateScale));
        EnergyHeatPolicy.Nonnegative(CoolingEnergyCostPerHeat, nameof(CoolingEnergyCostPerHeat));
        if (CoolingEnergyCostPerHeat == 0) throw new ArgumentOutOfRangeException(nameof(CoolingEnergyCostPerHeat), "Cooling must have a positive energy cost.");
    }
}

public readonly record struct EnergyHeatStepResult(EnergyHeatState State, float BaseEnergySpent,
    float HeatAdded, float HeatRemoved, float CoolingEnergySpent);

/// <summary>Pure arithmetic only. Canonical current resources live in the ASC.</summary>
public static class EnergyHeatPolicy
{
    internal static void Nonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
    }
    public static EnergyHeatState InitializeFull(EnergyHeatState state, float powerCapacity)
    {
        Nonnegative(powerCapacity, nameof(powerCapacity));
        return new(powerCapacity, state.Heat);
    }
    public static EnergyHeatStepResult Step(EnergyHeatState state, float powerCapacity, float coolingCapacity,
        float externalHeatLoad, float delta, EnergyHeatSettings settings)
    {
        Nonnegative(powerCapacity, nameof(powerCapacity)); Nonnegative(coolingCapacity, nameof(coolingCapacity));
        Nonnegative(externalHeatLoad, nameof(externalHeatLoad)); Nonnegative(delta, nameof(delta)); settings.Validate();
        // Wider intermediates keep even finite float-sized rates/deltas from overflowing.
        double energy = Math.Min(state.Energy, powerCapacity);
        double baseSpent = Math.Min(energy, (double)settings.BaseEnergyDrainPerSecond * delta);
        energy -= baseSpent;
        // Incoming heat saturates at the represented thermal maximum before thermal work.
        double heat = Math.Min(100, state.Heat + (double)externalHeatLoad * delta);
        double added = heat - state.Heat;
        double removed = Math.Min(heat, Math.Min((double)coolingCapacity * settings.CoolingRateScale * delta,
            energy / settings.CoolingEnergyCostPerHeat));
        double cost = removed * settings.CoolingEnergyCostPerHeat;
        return new(new((float)Math.Clamp(energy - cost, 0, powerCapacity), (float)Math.Clamp(heat - removed, 0, 100)),
            (float)baseSpent, (float)added, (float)removed, (float)cost);
    }
}
