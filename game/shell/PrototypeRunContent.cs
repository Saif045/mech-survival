using System;
using System.Linq;
using Godot;
using GameFactory.Networking.World;
using GameFactory.Shell;
using MechSurvival.Player;
using MechSurvival.Salvage;
using MechSurvival.Parasites;

namespace MechSurvival.Shell;

/// <summary>The single authored run. Shell and NetworkWorld retain all lifecycle ownership.</summary>
public partial class PrototypeRunContent : Node3D, IRoundContent
{
    [Export] public double DurationSeconds { get; set; } = 120;
    public static readonly Vector3 ExtractionCenter = new(0, 0, 8);
    public const float ExtractionRadius = 4.5f;
    public PrototypeRunRules? Rules { get; private set; }
    private NetworkWorld? _world;
    private SurveyBeacon? _beacon;
    private double _resultElapsed;
    private bool _returnRequested;
    private Label _hud = null!;
    private Label3D _extraction = null!;
    private StandardMaterial3D _marker = null!;

    public override void _Ready()
    {
        // Geometry is static and identical on every peer; no additional lifecycle.
        Box("Route", new(0, -0.2f, -4), new(9, 0.4f, 34), new(0.19f, 0.23f, 0.28f));
        Box("Start", new(0, -0.2f, 8), new(13, 0.4f, 12), new(0.19f, 0.29f, 0.3f));
        Box("SalvageBay", new(-7, -0.2f, -1), new(8, 0.4f, 8), new(0.3f, 0.25f, 0.16f));
        Box("WestNorthWall", new(-4.7f, 1.5f, -13), new(0.4f, 3, 16), Colors.SlateGray);
        Box("EastWall", new(4.7f, 1.5f, -7), new(0.4f, 3, 28), Colors.SlateGray);
        Box("EndWall", new(0, 1.5f, -21), new(10, 3, 0.4f), Colors.SlateGray);
        Box("BayWall", new(-11, 1.5f, -1), new(0.4f, 3, 8), Colors.SlateGray);
        Box("Crate", new(2.8f, 0.7f, -9), new(1.6f, 1.4f, 2), Colors.DarkSlateGray);
        Landmark("SALVAGE\nOptional: better Cooling / Power\nE: hack", new(-7, 3, -1), Colors.Orange);
        Landmark("OBJECTIVE\nReach survey beacon / E", new(0, 4, -18), Colors.Cyan);
        _extraction = Landmark("", new(0, 3, 8), Colors.Cyan);
        _marker = new StandardMaterial3D { AlbedoColor = Colors.DarkOrange, EmissionEnabled = true, Emission = Colors.DarkOrange };
        AddChild(new MeshInstance3D { Position = new(0, 0.03f, 8), Mesh = new CylinderMesh { TopRadius = ExtractionRadius, BottomRadius = ExtractionRadius, Height = 0.04f }, MaterialOverride = _marker });
        var canvas = new CanvasLayer(); AddChild(canvas);
        _hud = new Label { Position = new(16, 190), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.AddThemeFontSizeOverride("font_size", 24); canvas.AddChild(_hud);
    }
    private void Box(string name, Vector3 position, Vector3 size, Color color)
    {
        var body = new StaticBody3D { Name = name, Position = position }; AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color } });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
    }
    private Label3D Landmark(string text, Vector3 position, Color color)
    {
        var label = new Label3D { Text = text, Position = position, Modulate = color, FontSize = 40, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
        AddChild(label); return label;
    }
    public void StartRound(NetworkWorld world)
    {
        if (!Multiplayer.IsServer()) throw new InvalidOperationException("Only server gameplay starts a run.");
        _world = world; Rules = new(DurationSeconds); _resultElapsed = 0; _returnRequested = false;
        _beacon = SurveyBeacon.Spawn(world, new(Basis.Identity, new(0, 1, -18)), this);
        SalvageMachine.Spawn(world, new(Basis.Identity, new(-7, 1, -1)));
        Publish(0, 0);
    }
    internal bool ActivateObjective()
    {
        if (!Multiplayer.IsServer() || Rules is null || !Rules.ActivateObjective()) return false;
        // Five sites six units apart: each of the intended four players can
        // exclude at most one 2.75-unit site, so one is always clear.
        Vector3[] sites = [new(0, 1, -16), new(0, 1, -10), new(0, 1, -4), new(0, 1, 2), new(0, 1, 8)];
        var bodies = _world!.Objects.Select(o => o.Host).OfType<PlayableMech>().ToArray();
        Vector3 site = sites.First(p => bodies.All(b => b.GlobalPosition.DistanceTo(p) >= 2.75f));
        Parasite.Spawn(_world, new(Basis.Identity, site));
        Publish(0, bodies.Length); return true;
    }
    public override void _Process(double delta)
    {
        _world ??= GetParent().GetNodeOrNull<NetworkWorld>("NetworkWorld");
        if (_world is null) return;
        _beacon = _world.Objects.Select(o => o.Host).OfType<SurveyBeacon>().FirstOrDefault();
        if (Multiplayer.IsServer() && Rules is not null && _beacon is not null)
        {
            var bodies = _world.Objects.Where(o => o.IsBound && o.Host is PlayableMech b && b.IsInsideTree() && !b.IsQueuedForDeletion()).Select(o => (PlayableMech)o.Host).ToArray();
            int inside = bodies.Count(b => InsideExtraction(b.GlobalPosition));
            Rules.Advance(delta); Rules.CheckExtraction(bodies.Length, inside); Publish(inside, bodies.Length);
            if (Rules.Terminal)
            {
                _resultElapsed += delta;
                if (_resultElapsed >= 2 && !_returnRequested && GetParent().GetParent() is OnlineGameplayShell shell)
                {
                    _returnRequested = true;
                    _ = shell.ReturnToLobbyAsync();
                }
            }
        }
        var view = _beacon?.View;
        _hud.Visible = view is not null && _world.Objects.Any(o => o.Host is PlayableMech && o.OwnerPeerId.Value == Multiplayer.GetUniqueId());
        if (view is null) { _extraction.Text = "START / EXTRACTION\nEXTRACTION LOCKED"; return; }
        string extraction = view.ObjectiveComplete ? $"EXTRACTION ACTIVE\n{view.Inside} / {view.Active} players" : "EXTRACTION LOCKED";
        _extraction.Text = "START / EXTRACTION\n" + extraction;
        _marker.AlbedoColor = _marker.Emission = view.ObjectiveComplete ? Colors.LimeGreen : Colors.DarkOrange;
        int seconds = view.Seconds;
        _hud.Text = view.Phase switch
        {
            RunPhase.Success => "RUN COMPLETE",
            RunPhase.Failure => "RUN FAILED — TIME EXPIRED",
            _ => $"TIME {seconds / 60:00}:{seconds % 60:00}\n{(view.ObjectiveComplete ? "OBJECTIVE COMPLETE — return to START" : "OBJECTIVE: Reach survey beacon / E")}\n{extraction}"
        };
    }
    public static bool InsideExtraction(Vector3 position) => new Vector2(position.X - ExtractionCenter.X, position.Z - ExtractionCenter.Z).Length() <= ExtractionRadius && Math.Abs(position.Y) <= 4;
    private void Publish(int inside, int active) => _beacon!.Publish(new(Rules!.Phase, (int)Math.Ceiling(Rules.RemainingSeconds), Rules.ObjectiveComplete, inside, active));
    public void ResetRound()
    {
        Rules = null; _beacon = null; _resultElapsed = 0; _returnRequested = false;
        if (_hud is not null) _hud.Visible = false;
    }
}
