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
                if (open) { Wall(chunk, d * 11 + new Vector3(d.Z * 7, 0, d.X * 7), d.X == 0 ? new(8, 4, 0.4f) : new(0.4f, 4, 8)); Wall(chunk, d * 11 - new Vector3(d.Z * 7, 0, d.X * 7), d.X == 0 ? new(8, 4, 0.4f) : new(0.4f, 4, 8)); }
                else Wall(chunk, d * 11, d.X == 0 ? new(22, 4, 0.4f) : new(0.4f, 4, 22));
            }
        }
        GD.Print($"EXPEDITION seed={Seed} signature={Layout.Signature} rooms={Layout.Rooms.Count} connections={Layout.Rooms.Sum(r => r.Connections.Length) / 2}");
    }
    private void AddHazard(Node3D chunk, RoomEnvironment plan)
    {
        if (plan.Kind == EnvironmentKind.Safe) return;
        var color = plan.Kind switch { EnvironmentKind.Thermal => Colors.OrangeRed, EnvironmentKind.Electrical => Colors.Cyan, _ => Colors.GreenYellow };
        var material = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var floor = new MeshInstance3D { Position = new(0, 0.025f, 0), Mesh = new BoxMesh { Size = new(21, 0.025f, 21) }, MaterialOverride = material }; chunk.AddChild(floor);
        // Noncolliding silhouettes leave all authored navigation spokes clear.
        for (int n = 0; n < 3; n++) chunk.AddChild(new MeshInstance3D { Position = new(-8 + n * 2, 2, 8), Mesh = plan.Kind == EnvironmentKind.Corrosive ? new BoxMesh { Size = new(1.4f, 4, 1.4f) } : new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.6f, Height = 4 }, MaterialOverride = material });
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
            hazard.Label.Text = title + (active ? "\nACTIVE" : "\nDORMANT — STORM " + hazard.Plan.ActivationLevel);
        }
    }
    public override void _ExitTree() { if (Multiplayer.IsServer()) Environment?.Clear(); }
    private static void Wall(Node3D parent, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position + Vector3.Up * 2 }; parent.AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.SlateGray } });
    }
    internal void Publish(Observation view) { if (!Multiplayer.IsServer()) throw new InvalidOperationException("Server-owned run state."); RunState = JsonSerializer.Serialize(view); }
}
