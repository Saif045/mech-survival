using System;
using System.Linq;
using Godot;
using GameFactory.Networking.World;
using GameFactory.Shell;
using MechSurvival.Player;
using MechSurvival.Salvage;
using MechSurvival.Parasites;
using MechSurvival.Environment;
namespace MechSurvival.Shell;
/// <summary>Game-owned round composition; the replicated expedition owns static geometry lifetime.</summary>
public partial class PrototypeRunContent : Node3D, IRoundContent
{
    [Export] public double StormPhaseSeconds { get; set; } = ExpeditionEnvironmentPolicy.DefaultStormPhaseSeconds;
    [Export] public double DurationSeconds { get; set; } = 600;
    public int? ProbeSeed { get; set; }
    public const float ExtractionRadius = 4.5f;
    public PrototypeRunRules? Rules { get; private set; }
    public ExpeditionRun? Expedition => _world?.Objects.Select(o => o.Host).OfType<ExpeditionRun>().FirstOrDefault();
    public Vector3 ExtractionCenter => Expedition?.Center(Expedition.Layout.Extraction) ?? Vector3.Zero;
    private NetworkWorld? _world;
    private int? _previousSeed;
    private double _resultElapsed;
    private bool _returnRequested;
    private int _lastStorm;
    private double _stormAnnouncement;
    private Label _stormLabel = null!;
    private Label _hud = null!, _status = null!;
    private ProgressBar _energy = null!, _heat = null!;
    private CanvasLayer _canvas = null!;
    private double _emergencySeconds;
    private bool _wasEmergency;
    private Node3D _marker = null!;
    private Label3D _label = null!;
    public override void _Ready()
    {
        ProcessPriority = -100;
        var canvas = new CanvasLayer(); _canvas = canvas; AddChild(canvas);
        _hud = new Label { Position = new(16, 16), MouseFilter = Control.MouseFilterEnum.Ignore }; _hud.AddThemeFontSizeOverride("font_size", 22); canvas.AddChild(_hud);
        _stormLabel = new Label { Position = new(320, 50), Text = "STORM INTENSIFYING", Modulate = Colors.Orange, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _stormLabel.AddThemeFontSizeOverride("font_size", 30); canvas.AddChild(_stormLabel);
        canvas.AddChild(new Label { Position = new(20,88), Text = "ENERGY", MouseFilter = Control.MouseFilterEnum.Ignore });
        canvas.AddChild(new Label { Position = new(20,130), Text = "HEAT", MouseFilter = Control.MouseFilterEnum.Ignore });
        _energy = ResourceBar(canvas, 114); _heat = ResourceBar(canvas, 156);
        _status = new Label { Position = new(20,185), MouseFilter = Control.MouseFilterEnum.Ignore }; canvas.AddChild(_status);
        _marker = new Node3D(); AddChild(_marker);
        var material = new StandardMaterial3D { AlbedoColor = Colors.LimeGreen, EmissionEnabled = true, Emission = Colors.LimeGreen };
        _marker.AddChild(new MeshInstance3D { Position = new(0, 9, 0), Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.7f, Height = 18 }, MaterialOverride = material });
        _marker.AddChild(new MeshInstance3D { Position = new(0, 0.04f, 0), Mesh = new TorusMesh { InnerRadius = 3.8f, OuterRadius = 4.5f }, MaterialOverride = material });
        _label = new Label3D { Position = new(0, 12, 0), Text = "EXTRACTION ONLINE", FontSize = 80, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled }; _marker.AddChild(_label); _marker.Visible = false;
    }
    public void StartRound(NetworkWorld world)
    {
        if (!Multiplayer.IsServer()) throw new InvalidOperationException("Only server starts runs.");
        _world = world; Rules = new(DurationSeconds); _resultElapsed = 0; _returnRequested = false;
        int seed;
        do { seed = ProbeSeed ?? System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue); }
        while (ProbeSeed is null && seed == _previousSeed);
        _previousSeed = seed;
        var run = ExpeditionRun.Spawn(world, seed, StormPhaseSeconds);
        foreach (int room in run.Layout.Salvage) SalvageMachine.Spawn(world, new(Basis.Identity, run.Center(room) + new Vector3(4, 1, 4)));
        foreach (int room in run.Layout.Parasites) Parasite.Spawn(world, new(Basis.Identity, run.Center(room) + Vector3.Up));
        Publish(0, 0);
    }
    public override void _Process(double delta)
    {
        _world ??= GetParent().GetNodeOrNull<NetworkWorld>("NetworkWorld");
        var run = Expedition; if (run is null) { _canvas.Visible = false; _hud.Visible = false; _stormLabel.Visible = false; _marker.Visible = false; return; }
        var bodies = _world!.Objects.Where(o => o.IsBound && o.Host is PlayableMech b && b.IsInsideTree() && !b.IsQueuedForDeletion()).Select(o => (PlayableMech)o.Host).ToArray();
        if (Multiplayer.IsServer() && Rules is not null)
        {
            Rules.Advance(delta);
            run.Environment.Advance(delta, bodies, Rules.Terminal);
            foreach (var machine in _world.Objects.Select(o => o.Host).OfType<SalvageMachine>()) Rules.RecordSalvage(machine.NetworkObject.Id.Value, machine.State == SalvageMachineState.Disabled);
            int inside = bodies.Count(b => InsideExtraction(b.GlobalPosition)); Rules.CheckExtraction(bodies.Length, inside); Publish(inside, bodies.Length);
            if (Rules.Terminal) { _resultElapsed += delta; if (_resultElapsed >= 2 && !_returnRequested && GetParent().GetParent() is OnlineGameplayShell shell) { _returnRequested = true; _ = shell.ReturnToLobbyAsync(); } }
        }
        var view = run.View; var local = bodies.FirstOrDefault(b => b.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").OwnerPeerId.Value == Multiplayer.GetUniqueId());
        _canvas.Visible = local is not null && !local.GetNode<MechServiceController>("MechServiceController").PanelOpen;
        _hud.Visible = view is not null && local is not null; _marker.Position = ExtractionCenter; _marker.Visible = view?.ObjectiveComplete == true;
        if (view is null) return;
        if (view.StormLevel > _lastStorm) { _lastStorm = view.StormLevel; _stormAnnouncement = 5; }
        _stormAnnouncement = Math.Max(0, _stormAnnouncement - delta);
        _stormLabel.Visible = _hud.Visible && _stormAnnouncement > 0;
        var offset = ExtractionCenter - (local?.GlobalPosition ?? Vector3.Zero);
        string direction = (offset.Z < -3 ? "N" : offset.Z > 3 ? "S" : "") + (offset.X < -3 ? "W" : offset.X > 3 ? "E" : "");

        _label.Text = $"EXTRACTION ONLINE\n{view.Inside} / {view.Active} players";
        string pips = string.Join(" ", Enumerable.Range(0,3).Select(i => i < view.Salvage ? "●" : "○"));
        _hud.Text = view.Phase switch { RunPhase.Success => "RUN COMPLETE", RunPhase.Failure => "TIME EXPIRED", _ => $"SALVAGE  {pips}      {view.Seconds / 60:00}:{view.Seconds % 60:00}" + (view.ObjectiveComplete ? $"\nEXTRACTION {new Vector2(offset.X,offset.Z).Length():0}m {direction}" : "") };
        if (local is null) return;
        var gas = local.Gas.Snapshot;
        _energy.MaxValue = Math.Max(0.001f,gas.PowerCapacity); _energy.Value = gas.Energy;
        _heat.Value = gas.Heat;
        TintBar(_energy, gas.Energy / Math.Max(0.001f,gas.PowerCapacity) < 0.25f ? Colors.OrangeRed : Colors.MediumTurquoise);
        TintBar(_heat, gas.Heat >= 95 ? Colors.Red : gas.Heat >= 65 ? Colors.Orange : Colors.Goldenrod);
        var exposure = view.Exposures?.FirstOrDefault(e => e.BodyId == local.GetNode<GameFactory.Networking.Objects.NetworkObject>("NetworkObject").Id.Value);
        bool emergency = local.GasMoveSpeed <= MobilityMovementPolicy.EmergencyMinimum;
        if (emergency && !_wasEmergency) _emergencySeconds = 4;
        _wasEmergency = emergency; _emergencySeconds = Math.Max(0,_emergencySeconds-delta);
        _status.Text = (exposure is { Active: true } ? "! " + exposure.Kind.ToString().ToUpperInvariant() + "\n" : "")
            + (exposure?.Recovering == true ? "◇ STABILIZING\n" : "") + (exposure?.Damage ?? "")
            + (_emergencySeconds > 0 ? "\nEMERGENCY LOCOMOTION" : "");
    }
    private static ProgressBar ResourceBar(CanvasLayer canvas, float y)
    {
        var bar = new ProgressBar { Position = new(20,y), Size = new(250,12), MaxValue = 100, ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        canvas.AddChild(bar); return bar;
    }
    private static void TintBar(ProgressBar bar, Color color)
    {
        if (bar.GetThemeStylebox("fill") is StyleBoxFlat existing && existing.BgColor == color) return;
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color });
    }
    public bool InsideExtraction(Vector3 position) => new Vector2(position.X - ExtractionCenter.X, position.Z - ExtractionCenter.Z).Length() <= ExtractionRadius && Math.Abs(position.Y) <= 4;
    private void Publish(int inside, int active) => Expedition!.Publish(new(Rules!.Phase, (int)Math.Ceiling(Rules.RemainingSeconds), Rules.ObjectiveComplete, inside, active, Rules.SalvageCount, Expedition.Environment.StormLevel, Expedition.Environment.Exposures));
    public void ResetRound() { _emergencySeconds = 0; _wasEmergency = false; _lastStorm = 0; _stormAnnouncement = 0; if (_stormLabel is not null) _stormLabel.Visible = false; Expedition?.Environment.Clear(); Rules = null; _resultElapsed = 0; _returnRequested = false; if (_hud is not null) _hud.Visible = false; if (_marker is not null) _marker.Visible = false; }
}
