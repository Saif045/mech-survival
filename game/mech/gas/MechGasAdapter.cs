using System;
using Godot;

namespace MechSurvival.Mech.Gas;

/// <summary>The only C# / GodotGAS interop boundary for mechs.</summary>
internal sealed class MechGasAdapter
{
    private readonly Node _bridge;
    private MechGasAdapter(Node bridge) => _bridge = bridge;
    internal static MechGasAdapter Create(Node owner, bool authoritative)
    {
        var bridge = (Node)GD.Load<GDScript>("res://game/mech/gas/mech_gas_bridge.gd").New().AsGodotObject();
        bridge.Name = "GodotGasBridge";
        bridge.Call("configure_authority", authoritative);
        owner.AddChild(bridge);
        return new(bridge);
    }
    private static Godot.Collections.Array Values(MechGasSnapshot state) => [state.Mobility, state.PowerCapacity, state.CoolingCapacity];
    private static Godot.Collections.Array Tags(MechGasSnapshot state) => [state.HasMobility, state.HasSensor, state.HasPower, state.HasCooling];
    internal void UpdateStructure(MechGasSnapshot baseline)
    {
        if (!_bridge.Call("update_structure", Values(baseline), Tags(baseline)).AsBool())
            throw new InvalidOperationException("Authoritative GAS baseline effect was rejected.");
    }
    internal void ApplyMirror(MechGasSnapshot snapshot)
    {
        if (!_bridge.Call("apply_mirror", Values(snapshot), Tags(snapshot)).AsBool())
            throw new InvalidOperationException("GAS mirror rejected its authoritative snapshot.");
    }
    internal MechGasSnapshot Capture()
    {
        var data = _bridge.Call("capture_snapshot").AsGodotDictionary();
        return new(data["Mobility"].AsSingle(), data["PowerCapacity"].AsSingle(), data["CoolingCapacity"].AsSingle(),
            data["HasMobility"].AsBool(), data["HasSensor"].AsBool(), data["HasPower"].AsBool(), data["HasCooling"].AsBool());
    }
    internal bool SetDevelopmentModifier(bool enabled) => _bridge.Call("set_development_modifier",
        enabled ? GD.Load<GDScript>("res://game/probes/gas/mech_debug_modifier.gd").New() : default).AsBool();
    internal bool HasDevelopmentModifier => _bridge.Call("has_development_modifier").AsBool();
    internal bool UsesRealAsc => _bridge.Call("uses_real_asc").AsBool();
    internal bool ConsumeChanged() => _bridge.Call("consume_changed").AsBool();
    internal bool Shutdown() => _bridge.Call("shutdown").AsBool();
}
