using System;
using System.Text.Json;
using Godot;
using GameFactory.Gameplay.Interaction;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Objects.Components.Replication;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using MechSurvival.Player;

namespace MechSurvival.Shell;

public partial class SurveyBeacon : Node3D, INetworkSpawnInitializable, IInteractable
{
    public sealed record Observation(RunPhase Phase, int Seconds, bool ObjectiveComplete, int Inside, int Active);
    [Export, Replicated(ReplicationMode.OnChange)] public string RunState { get; private set; } = "";
    public Observation? View => RunState.Length == 0 ? null : JsonSerializer.Deserialize<Observation>(RunState);
    public NetworkObject NetworkObject => GetNode<NetworkObject>("NetworkObject");
    private PrototypeRunContent? _run;
    private StandardMaterial3D _material = null!;
    public override void _Ready()
    {
        AddToGroup("interactable");
        _material = new StandardMaterial3D { EmissionEnabled = true, EmissionEnergyMultiplier = 1.5f };
        GetNode<MeshInstance3D>("Mesh").MaterialOverride = _material;
        AddChild(new MeshInstance3D { Position = new(0, 2, 0), Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 1, Height = 2 }, MaterialOverride = _material });
        var status = GetNode<Label3D>("Status"); status.Position = new(0, 4, 0); status.FontSize = 44;
    }
    public static SurveyBeacon Spawn(NetworkWorld world, Transform3D transform, PrototypeRunContent run) => world.Spawn<SurveyBeacon>(
        GD.Load<PackedScene>("res://game/shell/survey_beacon.tscn"), PeerId.Server,
        new Godot.Collections.Dictionary { ["spawn_transform"] = transform }, beacon => beacon._run = run);
    public void ApplyNetworkSpawnData(Variant data) => Transform = data.AsGodotDictionary()["spawn_transform"].AsTransform3D();
    internal void Publish(Observation view)
    {
        if (!Multiplayer.IsServer()) throw new InvalidOperationException("Run observations are server-owned.");
        RunState = JsonSerializer.Serialize(view);
    }
    public bool CanInteract(InteractionContext context) => View is { ObjectiveComplete: false, Phase: RunPhase.RunningToObjective } &&
        context.Player.Host is PlayableMech && context.Player.IsBound && context.Player.World == NetworkObject.World;
    public void Interact(InteractionContext context)
    {
        if (!Multiplayer.IsServer() || !CanInteract(context) || context.Target != NetworkObject ||
            context.Player.OwnerPeerId != context.RequestingPeerId || context.Player.Host is not Node3D body ||
            body.GlobalPosition.DistanceTo(GlobalPosition) > 2.75f) return;
        _run!.ActivateObjective();
    }
    public override void _Process(double delta)
    {
        bool complete = View?.ObjectiveComplete == true;
        GetNode<Label3D>("Status").Text = complete ? "SURVEY COMPLETE\nEXTRACTION UNLOCKED\nRETURN TO START" : "SURVEY BEACON - E TO ACTIVATE\nPRIMARY OBJECTIVE";
        _material.AlbedoColor = _material.Emission = complete ? Colors.LimeGreen : Colors.Magenta;
    }
}
