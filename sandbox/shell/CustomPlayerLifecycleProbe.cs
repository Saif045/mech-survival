using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using GameFactory.Networking.Objects;
using GameFactory.Networking.Peers;
using GameFactory.Networking.Players;
using GameFactory.Networking.World;
using GameFactory.Shell;

namespace GameFactory.Sandbox.Shell;

/// <summary>Real Godot spawn/reset with synthetic registry peers; no Steam or VM required.</summary>
public partial class CustomPlayerLifecycleProbe : Node
{
    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        ENetMultiplayerPeer peer = new();
        OnlineGameplayWorld? gameplay = null;
        int result = 1;
        try
        {
            Require(peer.CreateServer(0) == Error.Ok, "Local ENet server could not start.");
            Multiplayer.MultiplayerPeer = peer;
            Node shell = GD.Load<PackedScene>("res://factory/shell/online_gameplay.tscn").Instantiate();
            gameplay = shell.GetNode<OnlineGameplayWorld>("OnlineGameplayWorld");
            shell.RemoveChild(gameplay);
            shell.Free();
            gameplay.PlayerScene = GD.Load<PackedScene>("res://sandbox/shell/custom_player_host.tscn");
            AddChild(gameplay);
            NetworkWorld world = gameplay.GetNode<NetworkWorld>("NetworkWorld");
            // Keep registry inspection in the probe rather than adding framework APIs for tests.
            PlayerRegistry players = Field<PlayerRegistry>(gameplay, "_players");
            PeerRegistry peers = Field<PeerRegistry>(gameplay, "_peers");
            long previousMax = 0;
            for (int round = 1; round <= 3; round++)
            {
                Require(gameplay.IsEmpty, "World must be empty before Start.");
                gameplay.Start();
                Require(world.Count == 3 && players.Count == 1, "Expected local player and two round objects.");
                CheckPlayer(world, players, PeerId.Server, 1, new Vector3(-2, 2, 0));
                PeerId remote = new(42);
                peers.Add(remote, isLocal: false);
                NetworkObject removed = CheckPlayer(world, players, remote, 2, new Vector3(2, 2, 0));
                Require(players.Count == 2 && world.Count == 4, "Remote registry peer did not spawn.");
                typeof(OnlineGameplayWorld).GetMethod("OnPeerDisconnected", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(gameplay, new object[] { remote.Value });
                Require(players.FindByPeer(remote) is null && !peers.Contains(remote), "Peer removal left registry state.");
                await Until(() => !world.TryGet(removed.Id, out _));
                Require(!GodotObject.IsInstanceValid(removed.Host), "Disconnected player host survived.");
                peers.Add(remote, isLocal: false);
                CheckPlayer(world, players, remote, 3, new Vector3(2, 2, 0));
                Require(world.Objects.All(obj => obj.Id.Value > previousMax), "Object IDs were reused.");
                previousMax = world.Objects.Max(obj => obj.Id.Value);

                NetworkObject[] playerObjects = players.Players.Select(player =>
                {
                    Require(world.TryGet(player.ObjectId, out NetworkObject? obj), "Player object is missing.");
                    return obj!;
                }).ToArray();
                Node[] remainingHosts = world.Objects.Except(playerObjects).Select(obj => obj.Host).ToArray();
                Require(remainingHosts.Length == 2, "Expected switch and carryable round objects.");
                bool teardownOrdered = true;
                int playerExits = 0;
                foreach (NetworkObject obj in playerObjects)
                {
                    ((CustomPlayerHost)obj.Host).OnExit = () =>
                    {
                        playerExits++;
                        teardownOrdered &= remainingHosts.All(host => GodotObject.IsInstanceValid(host) &&
                            host.IsInsideTree() && !host.IsQueuedForDeletion());
                    };
                }
                await gameplay.ResetRoundAsync();
                Require(teardownOrdered && playerExits == 2, "Round objects were queued before custom players exited.");
                Require(gameplay.IsEmpty && players.Count == 0 && peers.Count == 0 && world.Count == 0,
                    "Round reset left registries or objects active.");
                Require(playerObjects.All(obj => !GodotObject.IsInstanceValid(obj.Host)) &&
                    remainingHosts.All(host => !GodotObject.IsInstanceValid(host)), "Old round hosts survived.");
                Require(ReferenceEquals(Multiplayer.MultiplayerPeer, peer), "Round reset replaced transport.");
                GD.Print($"CUSTOM_PLAYER_LIFECYCLE_PASS round={round} max_id={previousMax} players_before_objects=true");
            }
            result = 0;
        }
        catch (Exception exception) { GD.PushError($"CUSTOM_PLAYER_LIFECYCLE_FAIL: {exception}"); }
        finally
        {
            gameplay?.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Multiplayer.MultiplayerPeer = null;
            peer.Close();
            peer.Dispose();
            GetTree().Quit(result);
        }
    }

    private static NetworkObject CheckPlayer(NetworkWorld world, PlayerRegistry players, PeerId peerId,
        long playerId, Vector3 position)
    {
        NetworkPlayer player = players.FindByPeer(peerId)
            ?? throw new InvalidOperationException("Player is not registered.");
        Require(player.Id.Value == playerId && player.PeerId == peerId &&
            ReferenceEquals(players.Find(player.Id), player), "Player registry identity mismatch.");
        Require(world.TryGet(player.ObjectId, out NetworkObject? obj), "Player object ID does not resolve.");
        Require(obj!.OwnerPeerId == peerId && obj.Host.GetMultiplayerAuthority() == PeerId.Server.Value,
            "Represented owner or server authority mismatch.");
        Require(obj.Host is CustomPlayerHost host && host.InitializedBeforeTree && host.SpawnPosition == position,
            "Custom Node host did not receive spawn-position initialization.");
        GD.Print($"CUSTOM_PLAYER_REGISTERED player_id={player.Id} peer_id={peerId} object_id={obj.Id}");
        return obj;
    }

    private async Task Until(Func<bool> condition)
    {
        ulong deadline = Time.GetTicksMsec() + 5000;
        while (!condition())
        {
            Require(Time.GetTicksMsec() < deadline, "Custom player despawn timed out.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static T Field<T>(OnlineGameplayWorld gameplay, string name) =>
        (T)typeof(OnlineGameplayWorld).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gameplay)!;

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
