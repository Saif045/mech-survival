using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MechSurvival.Mech;

namespace MechSurvival.Player;

public partial class MechServiceController
{
    public bool PanelOpen => _panel.Visible;
    private PanelContainer _panel = null!;
    private Label _compact = null!, _comparison = null!;
    private Button _install = null!, _detach = null!;
    private readonly Dictionary<MechSlot, ColorRect> _modules = new();
    private readonly Dictionary<MechSlot, ProgressBar> _conditionBars = new();
    private Control _schematic = null!;
    private Label _resources = null!;
    private readonly Dictionary<MechSlot, Label> _rows = new();
    private readonly Dictionary<MechSlot, Button> _selectors = new();
    private void BuildPanel()
    {
        var canvas = GetNode<CanvasLayer>("HUD"); canvas.Layer = 20;
        _compact = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(_compact); _compact.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _compact.OffsetLeft = -320; _compact.OffsetRight = -16;
        _compact.OffsetTop = 190; _compact.OffsetBottom = 255;
        _compact.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _compact.AddThemeFontSizeOverride("font_size", 14);
        _schematic = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(_schematic); _schematic.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _schematic.Position += new Vector2(-150, 40);
        var positions = new Dictionary<MechSlot, Vector2> {
            [MechSlot.Sensor] = new(46,0), [MechSlot.Torso] = new(36,26), [MechSlot.Core] = new(46,57),
            [MechSlot.LeftArm] = new(8,28), [MechSlot.RightArm] = new(92,28),
            [MechSlot.Power] = new(34,80), [MechSlot.Cooling] = new(68,80),
            [MechSlot.LeftLeg] = new(32,106), [MechSlot.RightLeg] = new(70,106) };
        foreach (var slot in MechBody.Slots) {
            var module = new ColorRect { Position = positions[slot], Size = slot == MechSlot.Torso ? new(50,26) : new(24,22), MouseFilter = Control.MouseFilterEnum.Ignore };
            _schematic.AddChild(module); _modules.Add(slot, module);
        }
        _panel = new PanelContainer { Visible = false }; canvas.AddChild(_panel);
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _panel.OffsetLeft = -510; _panel.OffsetRight = 510; _panel.OffsetTop = -310; _panel.OffsetBottom = 310;
        var margin = new MarginContainer(); _panel.AddChild(margin);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 14);
        var column = new VBoxContainer(); margin.AddChild(column);
        column.AddChild(new Label { Text = "MECH STATUS / SERVICE    |    TAB to close    |    Game continues", MouseFilter = Control.MouseFilterEnum.Ignore });
        _resources = new Label(); column.AddChild(_resources);
        var group = new ButtonGroup();
        foreach (var slot in MechBody.Slots)
        {
            var row = new HBoxContainer(); column.AddChild(row);
            var select = new Button { Text = MechPartReadout.SlotName(slot), CustomMinimumSize = new(125, 39), ToggleMode = true, ButtonGroup = group };
            row.AddChild(select); select.Pressed += () => SelectSlot(slot); _selectors.Add(slot, select);
            var bar = new ProgressBar { CustomMinimumSize = new(110, 20), MaxValue = 1, ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddChild(bar); _conditionBars.Add(slot, bar);
            var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore }; label.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(label); _rows.Add(slot, label);
        }
        column.AddChild(new HSeparator());
        _comparison = new Label { CustomMinimumSize = new(0, 118), MouseFilter = Control.MouseFilterEnum.Ignore };
        _comparison.AddThemeFontSizeOverride("font_size", 15); column.AddChild(_comparison);
        var actions = new HBoxContainer(); column.AddChild(actions);
        _install = new Button { Text = "INSTALL / REPLACE SELECTED", CustomMinimumSize = new(320, 36) };
        _detach = new Button { Text = "DETACH SELECTED", CustomMinimumSize = new(250, 36) };
        actions.AddChild(_install); actions.AddChild(_detach);
        var close = new Button { Text = "CLOSE", CustomMinimumSize = new(120, 36) }; actions.AddChild(close);
        _install.Pressed += () => RequestInstall(SelectedSlot);
        _detach.Pressed += () => RequestDetach(SelectedSlot);
        close.Pressed += () => SetPanelOpen(false);
        RefreshPanel();
    }
    internal void SelectSlot(MechSlot slot)
    {
        if (!Enum.IsDefined(slot)) return;
        SelectedSlot = slot; RefreshPanel();
    }
    internal void SetPanelOpen(bool open)
    {
        _panel.Visible = open && IsLocalOwner();
        if (IsLocalOwner()) Input.MouseMode = _panel.Visible ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        RefreshPanel();
    }
    private void RefreshPanel()
    {
        if (_compact is null) return;
        bool owner = IsLocalOwner();
        if (!owner) _panel.Visible = false;
        _compact.Visible = owner && !PanelOpen;
        var carried = _actor.IsBound ? _actor.World.Objects.Select(o => o.Host).OfType<LooseMechPart>()
            .FirstOrDefault(p => p.Carry.HolderNetworkObjectId == _actor.Id.Value) : null;
        var definition = carried?.Catalog.Freeze().Resolve(carried.Projection.Model);

        _schematic.Visible = owner && !PanelOpen;
        foreach (var slot in MechBody.Slots) {
            var part = _body.View.GetPart(slot);
            _modules[slot].Color = part is null || part.Condition <= 0 ? new Color(0.18f,0.2f,0.23f) : part.Condition < 0.25f ? Colors.OrangeRed : part.Condition < 0.65f ? Colors.Goldenrod : Colors.MediumTurquoise;
        }
        _compact.Text = "TAB · MECH" + (carried is null ? "" : "\nCARRYING · " + MechPartReadout.Name(carried.Projection.Model));
        if (!PanelOpen) return;
        _resources.Text = $"ENERGY {_body.Gas.Snapshot.Energy:F1} / {_body.Gas.Snapshot.PowerCapacity:F1}     HEAT {_body.Gas.Snapshot.Heat:F1}";
        foreach (var slot in MechBody.Slots)
        {
            _conditionBars[slot].Value = _body.View.GetPart(slot)?.Condition ?? 0;
            _rows[slot].Text = MechPartReadout.Installed(_body.View, slot);
            _selectors[slot].SetPressedNoSignal(slot == SelectedSlot);
        }
        var current = _body.View.GetPart(SelectedSlot);
        bool compatible = definition?.Supports(SelectedSlot) == true;
        _install.Disabled = !compatible;
        _detach.Disabled = current is null;
        _comparison.Text = carried is null ? $"SELECTED: {MechPartReadout.SlotName(SelectedSlot)}\nNo part carried. E: pick up a world part / Q: drop.\nInstall requires a carried compatible part. Detach is always deliberate."
            : $"CARRIED: {MechPartReadout.Name(definition!.Model)}  Condition {carried.Projection.Condition:P0}\nCompatible: {string.Join(", ", definition.CompatibleSlots.Select(MechPartReadout.SlotName))}\nCURRENT: {(current is null ? "EMPTY" : MechPartReadout.Name(current.Definition.Model) + $" {current.Condition:P0} " + MechPartReadout.Describe(current.Definition, current.Condition))}\nCARRIED: {MechPartReadout.Describe(definition, carried.Projection.Condition)}\n{(compatible ? "CHANGE: " + MechPartReadout.Change(current, definition, carried.Projection.Condition) : "Select a compatible slot to install.")}";
    }
}
