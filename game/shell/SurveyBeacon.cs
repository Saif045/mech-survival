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
    public override void _Process(double delta) => GetNode<Label3D>("Status").Text = View?.ObjectiveComplete == true ? "OBJECTIVE COMPLETE\nReturn to extraction" : "SURVEY BEACON — INACTIVE\nE: activate objective";
}
