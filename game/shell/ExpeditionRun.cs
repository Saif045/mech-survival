using System;
using System.Linq;
using System.Text.Json;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using MechSurvival.World;
using MechSurvival.Environment;
using System.Collections.Generic;
namespace MechSurvival.Shell;
public partial class ExpeditionRun : Node3D, INetworkSpawnInitializable
{
    public sealed record Observation(RunPhase Phase, int Seconds, bool ObjectiveComplete, int Inside, int Active, int Salvage, int StormLevel = 0, ExpeditionEnvironment.Exposure[]? Exposures = null);
    [Export, Replicated(ReplicationMode.OnChange)] public string RunState { get; private set; } = "";
    public Observation? View => RunState.Length == 0 ? null : JsonSerializer.Deserialize<Observation>(RunState);
    public NetworkObject NetworkObject => GetNode<NetworkObject>("NetworkObject");
    public ExpeditionLayout Layout { get; private set; } = null!;
    public ExpeditionEnvironmentPolicy EnvironmentPlan { get; private set; } = null!;
    public ExpeditionEnvironment Environment { get; private set; } = null!;
    private readonly List<(MeshInstance3D Mesh, StandardMaterial3D Material, Label3D Label, RoomEnvironment Plan)> _hazards = new();
    public int Seed { get; private set; }
    private bool _initialized;
    public static ExpeditionRun Spawn(NetworkWorld world, int seed, double stormPhaseSeconds = ExpeditionEnvironmentPolicy.DefaultStormPhaseSeconds) => world.Spawn<ExpeditionRun>(GD.Load<PackedScene>("res://game/shell/expedition_run.tscn"), PeerId.Server, new Godot.Collections.Dictionary { ["seed"] = seed, ["storm_phase_seconds"] = stormPhaseSeconds });
    public void ApplyNetworkSpawnData(Variant data)
    {
        if (_initialized || IsInsideTree()) throw new InvalidOperationException("Expedition initializes once before tree entry.");
        Seed = data.AsGodotDictionary()["seed"].AsInt32();
        Layout = ExpeditionLayout.Generate(Seed);
        EnvironmentPlan = new(Layout, data.AsGodotDictionary()["storm_phase_seconds"].AsDouble());
        Environment = new(this);
        _initialized = true;
    }
    public static Vector3 Center(ExpeditionLayout.Room room) => new(room.X * ExpeditionLayout.CellSize, 0, room.Z * ExpeditionLayout.CellSize);
    public Vector3 Center(int room) => Center(Layout.Rooms[room]);
    public int RoomAt(Vector3 position) => Enumerable.Range(0, Layout.Rooms.Count).OrderBy(i => new Vector2(position.X - Center(i).X, position.Z - Center(i).Z).LengthSquared()).First();
    public override void _Ready()
    {
        if (!_initialized) throw new InvalidOperationException("Expedition requires its authoritative seed.");
        string[] templates = { "yard", "storage", "maintenance", "machinery", "loading", "damaged" };
        for (int i = 0; i < Layout.Rooms.Count; i++)
        {
            var room = Layout.Rooms[i]; var chunk = GD.Load<PackedScene>($"res://game/world/rooms/{templates[room.Template]}.tscn").Instantiate<Node3D>();
            chunk.Position = Center(i); AddChild(chunk);
            AddHazard(chunk, EnvironmentPlan.Rooms[i]);
            var cardinal = new[] { new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(-1, 0, 0) };
            foreach (var d in cardinal)
            {
                bool open = room.Connections.Any(n => Center(n) - Center(i) == d * ExpeditionLayout.CellSize);
                // Low industrial banks preserve sightlines; blocked edges retain graph collision.
                if (open) { Wall(chunk, d * 21 + new Vector3(d.Z * 14.5f, 0, d.X * 14.5f), d.X == 0 ? new(13, 2, 2) : new(2, 2, 13), room.Template); Wall(chunk, d * 21 - new Vector3(d.Z * 14.5f, 0, d.X * 14.5f), d.X == 0 ? new(13, 2, 2) : new(2, 2, 13), room.Template); }
                else Wall(chunk, d * 21, d.X == 0 ? new(42, 2.6f, 3) : new(3, 2.6f, 42), room.Template);
            }
        }
        GD.Print($"EXPEDITION seed={Seed} signature={Layout.Signature} rooms={Layout.Rooms.Count} connections={Layout.Rooms.Sum(r => r.Connections.Length) / 2}");
    }
    private void AddHazard(Node3D chunk, RoomEnvironment plan)
    {
        if (plan.Kind == EnvironmentKind.Safe) return;
        var color = plan.Kind switch { EnvironmentKind.Thermal => Colors.OrangeRed, EnvironmentKind.Electrical => Colors.Cyan, _ => Colors.GreenYellow };
        var material = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var floor = new MeshInstance3D { Position = new(0, 0.025f, 0), Mesh = new BoxMesh { Size = new(40, 0.025f, 40) }, MaterialOverride = material }; chunk.AddChild(floor);
        // Noncolliding silhouettes leave all authored navigation spokes clear.
        for (int n = 0; n < 3; n++) chunk.AddChild(new MeshInstance3D { Position = new(-14 + n * 3, 2, 14), Mesh = plan.Kind switch { EnvironmentKind.Corrosive => new SphereMesh { Radius = 2, Height = 3 }, EnvironmentKind.Electrical => new BoxMesh { Size = new(1.5f,4,1.5f) }, _ => new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.2f, Height = 2 } }, MaterialOverride = material });
        var label = new Label3D { Position = new(0, 6, 0), FontSize = 54, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = color }; chunk.AddChild(label);
        _hazards.Add((floor, material, label, plan));
    }
    public override void _Process(double delta)
    {
        int level = View?.StormLevel ?? 0;
        foreach (var hazard in _hazards)
        {
            bool active = hazard.Plan.Active(level);
            var color = hazard.Material.AlbedoColor; color.A = active ? 0.18f + 0.05f * MathF.Sin(Time.GetTicksMsec() * 0.004f) : 0.045f;
            hazard.Material.AlbedoColor = color; hazard.Material.EmissionEnergyMultiplier = active ? 1.5f : 0.15f;
            string title = hazard.Plan.Kind switch { EnvironmentKind.Thermal => "THERMAL LEAK", EnvironmentKind.Electrical => "ELECTRICAL INTERFERENCE", _ => "CORROSIVE ATMOSPHERE" };
            hazard.Label.Text = active ? title : "";
        }
    }
    public override void _ExitTree() { if (Multiplayer.IsServer()) Environment?.Clear(); }
    private static void Wall(Node3D parent, Vector3 position, Vector3 size, int style)
    {
        var body = new StaticBody3D { Position = position + Vector3.Up * (size.Y / 2) }; parent.AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        var material = new StandardMaterial3D { AlbedoColor = style switch {
            0 => new(0.32f,0.29f,0.22f), 1 => new(0.25f,0.35f,0.38f), 2 => new(0.4f,0.28f,0.17f),
            3 => new(0.27f,0.31f,0.3f), 4 => new(0.42f,0.35f,0.18f), _ => new(0.25f,0.24f,0.27f) } };
        bool horizontal = size.X > size.Z;
        int sections = (int)Math.Max(size.X,size.Z) / 6;
        for (int n=0;n<sections;n++)
        {
            float along = (n-(sections-1)*0.5f)*6;
            Vector3 offset = horizontal ? new(along,0,0) : new(0,0,along);
            var mesh = new MeshInstance3D { Position = offset, MaterialOverride = material };
            if (style == 2) {
                mesh.Mesh = new CylinderMesh { TopRadius = size.Y/2, BottomRadius = size.Y/2, Height = 6.1f };
                mesh.Rotation = horizontal ? new(0,0,Mathf.Pi/2) : new(Mathf.Pi/2,0,0);
            } else if (style == 3) {
                mesh.Mesh = new CylinderMesh { TopRadius = 2.4f, BottomRadius = 2.4f, Height = size.Y + (n%2)*1.2f };
            } else {
                mesh.Mesh = new BoxMesh { Size = horizontal ? new(6.1f,size.Y+(n%2)*0.35f,size.Z) : new(size.X,size.Y+(n%2)*0.35f,6.1f) };
                if (style == 0 || style == 5) mesh.Rotation = new(0,0,(n%2==0?1:-1)*0.12f);
            }
            body.AddChild(mesh);
        }
    }
    internal void Publish(Observation view) { if (!Multiplayer.IsServer()) throw new InvalidOperationException("Server-owned run state."); RunState = JsonSerializer.Serialize(view); }
}
