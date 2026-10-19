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
    [Export] public double DurationSeconds { get; set; } = 600;
    public static readonly Vector3 ExtractionCenter = new(0, 0, 8);
    public const float ExtractionRadius = 4.5f;
    public PrototypeRunRules? Rules { get; private set; }
    private NetworkWorld? _world;
    private SurveyBeacon? _beacon;
    private double _resultElapsed;
    private bool _returnRequested;
    public static readonly Vector3 ObjectivePosition = new(0, 1, -100);
    private bool _announced;
    private double _announcementRemaining;
    private Label _announcement = null!;
    private Label _hud = null!;
    private Label3D _extraction = null!;
    private StandardMaterial3D _marker = null!;

    public override void _Ready()
    {
        // Geometry is static and identical on every peer; no additional lifecycle.
        Box("Route", new(0, -0.2f, -48), new(12, 0.4f, 114), new(0.19f, 0.23f, 0.28f));
        Box("StartYard", new(0, -0.2f, 6), new(18, 0.4f, 18), new(0.19f, 0.29f, 0.3f));
        Box("SalvageBayA", new(-8, -0.2f, -1), new(10, 0.4f, 10), new(0.3f, 0.25f, 0.16f));
        Box("CrateYard", new(0, -0.18f, -22), new(22, 0.4f, 20), new(0.3f, 0.26f, 0.18f));
        Box("SalvageBayB", new(10, -0.2f, -31), new(10, 0.4f, 10), new(0.3f, 0.25f, 0.16f));
        Box("Connector", new(0, -0.16f, -48), new(10, 0.4f, 32), new(0.16f, 0.21f, 0.3f));
        Box("IndustrialYard", new(0, -0.18f, -73), new(24, 0.4f, 22), new(0.18f, 0.3f, 0.27f));
        Box("SalvageBayC", new(-10, -0.2f, -73), new(10, 0.4f, 10), new(0.3f, 0.25f, 0.16f));
        Box("ObjectiveFacility", new(0, -0.16f, -94), new(16, 0.4f, 24), new(0.22f, 0.22f, 0.33f));
        Box("ConnectorWest", new(-5.2f, 1.5f, -48), new(0.4f, 3, 30), Colors.SlateGray);
        Box("ConnectorEast", new(5.2f, 1.5f, -48), new(0.4f, 3, 30), Colors.SlateGray);
        Box("FacilityWest", new(-8, 2, -94), new(0.4f, 4, 24), Colors.DarkSlateBlue);
        Box("FacilityEast", new(8, 2, -94), new(0.4f, 4, 24), Colors.DarkSlateBlue);
        Box("EndWall", new(0, 2, -105), new(16, 4, 0.4f), Colors.SlateGray);
        Box("BayWall", new(-13, 1.5f, -1), new(0.4f, 3, 10), Colors.SlateGray);
        Box("BayBWall", new(15, 1.5f, -31), new(0.4f, 3, 10), Colors.SlateGray);
        Box("BayCWall", new(-15, 1.5f, -73), new(0.4f, 3, 10), Colors.SlateGray);
        foreach (float z in new[] { -17f, -25f, -69f, -77f })
        {
            Box("CrateWest" + -z, new(-4, 1, z), new(3, 2, 4), Colors.DarkGoldenrod);
            Box("CrateEast" + -z, new(4, 0.75f, z - 2), new(2, 1.5f, 3), Colors.DarkSlateGray);
        }
        Landmark("START YARD", new(0, 4, 1), Colors.Cyan);
        Landmark("CRATE YARD", new(0, 4, -22), Colors.Orange);
        Landmark("LONG CONNECTOR", new(0, 4, -48), Colors.LightBlue);
        Landmark("INDUSTRIAL YARD", new(0, 4, -73), Colors.Turquoise);
        Landmark("OBJECTIVE FACILITY\nSURVEY BEACON AHEAD", new(0, 5, -88), Colors.Magenta);
        foreach (var site in new[] { new Vector3(-7, 3, -1), new Vector3(10, 3, -31), new Vector3(-10, 3, -73) })
            Landmark("SALVAGE BAY\nOptional Cooling / Power upgrades\nE: attempt salvage", site, Colors.Orange);
        _extraction = Landmark("", new(0, 3, 8), Colors.Cyan);
        _marker = new StandardMaterial3D { AlbedoColor = Colors.DarkOrange, EmissionEnabled = true, Emission = Colors.DarkOrange };
        AddChild(new MeshInstance3D { Position = new(0, 0.03f, 8), Mesh = new CylinderMesh { TopRadius = ExtractionRadius, BottomRadius = ExtractionRadius, Height = 0.04f }, MaterialOverride = _marker });
        var canvas = new CanvasLayer(); AddChild(canvas);
        _hud = new Label { Position = new(16, 16), MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.AddThemeFontSizeOverride("font_size", 22); canvas.AddChild(_hud);
        _announcement = new Label { Position = new(320, 50), Modulate = Colors.Cyan, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _announcement.AddThemeFontSizeOverride("font_size", 30); canvas.AddChild(_announcement);
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
        _beacon = SurveyBeacon.Spawn(world, new(Basis.Identity, ObjectivePosition), this);
        SalvageMachine.Spawn(world, new(Basis.Identity, new(-7, 1, -1)));
        SalvageMachine.Spawn(world, new(Basis.Identity, new(10, 1, -31)));
        SalvageMachine.Spawn(world, new(Basis.Identity, new(-10, 1, -73)));
        Publish(0, 0);
    }
    internal bool ActivateObjective()
    {
        if (!Multiplayer.IsServer() || Rules is null || !Rules.ActivateObjective()) return false;
        var bodies = _world!.Objects.Select(o => o.Host).OfType<PlayableMech>().ToArray();
        // Each separated return zone has five candidates six units apart.
        // Four players can exclude at most four candidates, leaving one clear.
        foreach (float startZ in new[] { -80f, -44f })
        {
            var site = Enumerable.Range(0, 5).Select(i => new Vector3(0, 1, startZ + 6 * i))
                .First(p => bodies.All(b => b.GlobalPosition.DistanceTo(p) >= 2.75f));
            Parasite.Spawn(_world, new(Basis.Identity, site));
        }
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
        if (view?.ObjectiveComplete == true && !_announced)
        {
            _announced = true; _announcementRemaining = 5;
            _announcement.Text = "SURVEY COMPLETE\nEXTRACTION UNLOCKED\nPARASITE ACTIVITY DETECTED\nRETURN TO START";
        }
        _announcementRemaining = Math.Max(0, _announcementRemaining - delta);
        _announcement.Visible = _hud.Visible && _announcementRemaining > 0;
        if (view is null) { _extraction.Text = "START / EXTRACTION\nEXTRACTION LOCKED"; return; }
        string extraction = view.ObjectiveComplete ? $"EXTRACTION ACTIVE\n{view.Inside} / {view.Active} players" : "EXTRACTION LOCKED - activate beacon";
        _extraction.Text = "START / EXTRACTION\n" + extraction;
        _marker.AlbedoColor = _marker.Emission = view.ObjectiveComplete ? Colors.LimeGreen : Colors.DarkOrange;
        int seconds = view.Seconds;
        _hud.Text = view.Phase switch
        {
            RunPhase.Success => "RUN COMPLETE",
            RunPhase.Failure => "RUN FAILED — TIME EXPIRED",
            _ => $"TIME {seconds / 60:00}:{seconds % 60:00}\n{(view.ObjectiveComplete ? "OBJECTIVE COMPLETE / RETURN TO EXTRACTION" : "PRIMARY OBJECTIVE: Activate the Survey Beacon / E")}\n{extraction}"
        };
    }
    public static bool InsideExtraction(Vector3 position) => new Vector2(position.X - ExtractionCenter.X, position.Z - ExtractionCenter.Z).Length() <= ExtractionRadius && Math.Abs(position.Y) <= 4;
    private void Publish(int inside, int active) => _beacon!.Publish(new(Rules!.Phase, (int)Math.Ceiling(Rules.RemainingSeconds), Rules.ObjectiveComplete, inside, active));
    public void ResetRound()
    {
        _announced = false; _announcementRemaining = 0;
        if (_announcement is not null) _announcement.Visible = false;
        Rules = null; _beacon = null; _resultElapsed = 0; _returnRequested = false;
        if (_hud is not null) _hud.Visible = false;
    }
}
