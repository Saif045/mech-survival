using System;
using System.Collections.Generic;
using Godot;
using MechSurvival.Mech;

namespace MechSurvival.Player;

/// <summary>Every module directly observes the existing immutable body view.</summary>
public partial class MechModulePresentation : Node3D
{
    private NetworkMechBody _body = null!;
    private readonly Dictionary<MechSlot, MeshInstance3D> _modules = new();
    public override void _Ready()
    {
        _body = GetParent().GetParent().GetParent<NetworkMechBody>();
        Module(MechSlot.Torso, new(0, 0.15f, 0), new(0.9f, 0.8f, 0.65f), Colors.SlateGray);
        Module(MechSlot.Sensor, new(0, 0.82f, -0.12f), new(0.5f, 0.28f, 0.5f), Colors.Yellow);
        Module(MechSlot.Core, new(0, -0.25f, -0.4f), new(0.34f, 0.3f, 0.2f), Colors.Cyan);
        Module(MechSlot.LeftArm, new(-0.77f, 0, 0), new(0.38f, 0.85f, 0.42f), Colors.DarkOrange);
        Module(MechSlot.RightArm, new(0.77f, 0, 0), new(0.38f, 0.85f, 0.42f), Colors.DarkOrange);
        Module(MechSlot.LeftLeg, new(-0.32f, -0.66f, 0), new(0.35f, 0.64f, 0.5f), Colors.DodgerBlue);
        Module(MechSlot.RightLeg, new(0.32f, -0.66f, 0), new(0.35f, 0.64f, 0.5f), Colors.DodgerBlue);
        Module(MechSlot.Power, new(-0.27f, -0.3f, 0.4f), new(0.32f, 0.36f, 0.3f), Colors.MediumPurple);
        Module(MechSlot.Cooling, new(0.27f, -0.3f, 0.4f), new(0.32f, 0.36f, 0.3f), Colors.Turquoise);
        _body.Changed += Observe; Observe();
    }
    private void Module(MechSlot slot, Vector3 position, Vector3 size, Color color)
    {
        var mesh = new MeshInstance3D { Name = slot.ToString(), Position = position, Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.15f } };
        AddChild(mesh); _modules.Add(slot, mesh);
        mesh.SetMeta("category_color", color);
    }
    private void Observe()
    {
        foreach (var (slot, mesh) in _modules)
        {
            var part = _body.View.GetPart(slot); mesh.Visible = part is not null;
            var material = (StandardMaterial3D)mesh.MaterialOverride;
            var color = mesh.GetMeta("category_color").AsColor();
            material.AlbedoColor = color * (0.25f + 0.75f * (part?.Condition ?? 0));
            material.EmissionEnergyMultiplier = 0.2f * (part?.Condition ?? 0);
        }
    }
    public override void _ExitTree() { if (_body is not null) _body.Changed -= Observe; }
}
