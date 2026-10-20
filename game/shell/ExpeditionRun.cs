using System;
using System.Linq;
using System.Text.Json;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using MechSurvival.World;
namespace MechSurvival.Shell;
public partial class ExpeditionRun : Node3D, INetworkSpawnInitializable
{
    public sealed record Observation(RunPhase Phase, int Seconds, bool ObjectiveComplete, int Inside, int Active, int Salvage);
    [Export, Replicated(ReplicationMode.OnChange)] public string RunState { get; private set; } = "";
    public Observation? View => RunState.Length == 0 ? null : JsonSerializer.Deserialize<Observation>(RunState);
    public NetworkObject NetworkObject => GetNode<NetworkObject>("NetworkObject");
    public ExpeditionLayout Layout { get; private set; } = null!;
    public int Seed { get; private set; }
    private bool _initialized;
    public static ExpeditionRun Spawn(NetworkWorld world, int seed) => world.Spawn<ExpeditionRun>(GD.Load<PackedScene>("res://game/shell/expedition_run.tscn"), PeerId.Server, new Godot.Collections.Dictionary { ["seed"] = seed });
    public void ApplyNetworkSpawnData(Variant data)
    {
        if (_initialized || IsInsideTree()) throw new InvalidOperationException("Expedition initializes once before tree entry.");
        Seed = data.AsGodotDictionary()["seed"].AsInt32();
        Layout = ExpeditionLayout.Generate(Seed);
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
    private static void Wall(Node3D parent, Vector3 position, Vector3 size)
    {
        var body = new StaticBody3D { Position = position + Vector3.Up * 2 }; parent.AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.SlateGray } });
    }
    internal void Publish(Observation view) { if (!Multiplayer.IsServer()) throw new InvalidOperationException("Server-owned run state."); RunState = JsonSerializer.Serialize(view); }
}
