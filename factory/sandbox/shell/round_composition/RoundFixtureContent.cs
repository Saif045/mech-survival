using System;
using Godot;
using GameFactory.Diagnostics;
using GameFactory.Networking.Peers;
using GameFactory.Networking.World;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell.RoundComposition;

public partial class RoundFixtureContent : Node, IRoundContent
{
    public int Starts { get; private set; }
    public int Resets { get; private set; }
    public int LocalRoundValue { get; private set; }
    public bool Subscribed { get; private set; }
    public int LastResetWorldCount { get; private set; }
    private NetworkWorld? _world;
    public void StartRound(NetworkWorld world)
    {
        if (Subscribed) throw new InvalidOperationException("Round state was not reset.");
        _world = world;
        LocalRoundValue = ++Starts;
        Subscribed = true;
        GameLog.EntryWritten += Observe;
        foreach (string kind in new[] { "beacon", "crate" })
            world.Spawn<RoundFixtureObject>(GD.Load<PackedScene>($"res://factory/sandbox/shell/round_composition/{kind}.tscn"), PeerId.Server,
                new Godot.Collections.Dictionary
                {
                    ["kind"] = kind, ["round_value"] = LocalRoundValue,
                    ["spawn_position"] = kind == "beacon" ? new Vector3(0, 1, 5) : new Vector3(2, 1, 5)
                });
    }
    private void Observe(LogEntry entry) { } // A real scoped subscription whose lifetime is asserted.
    public void ResetRound()
    {
        LastResetWorldCount = _world?.Count ?? 0;
        _world = null;
        GameLog.EntryWritten -= Observe;
        Subscribed = false;
        LocalRoundValue = 0;
        Resets++;
    }
}
