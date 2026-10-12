using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using GameFactory.Shell;
using GameFactory.Steam.Models;

namespace GameFactory.Sandbox.Shell;

/// <summary>Exercise normal shell entry methods and the real Maaack loader without opening Steam sessions.</summary>
public partial class OnlineGameplayRoutingProbe : Node
{
    private const string Setting = "maaacks_game_template/game_scene_path";
    private const string CustomScene = "res://sandbox/shell/routing_gameplay_fixture.tscn";
    private int _loads;

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        bool hadSetting = ProjectSettings.HasSetting(Setting);
        Variant original = ProjectSettings.GetSetting(Setting, "");
        Node loader = GetNode("/root/SceneLoader");
        Callable loaded = Callable.From(() => _loads++);
        GameShell shell = GetNode<GameShell>("/root/GameShell");
        int result = 1;
        // Retain this controller while SceneLoader replaces the current scene.
        GetTree().CurrentScene = null;
        loader.Connect("scene_loaded", loaded);
        try
        {
            Require(!shell.TryConsumeOnlineGameplayLaunchIntent(out _), "Unexpected initial launch intent.");
            Node menu = GD.Load<PackedScene>(GameShell.MainMenuScenePath).Instantiate();
            GetTree().Root.AddChild(menu);
            GetTree().CurrentScene = menu;
            foreach (string path in new[] { GameShell.OnlineGameplayScenePath, "", "res://missing_gameplay_scene.tscn" })
            {
                ProjectSettings.SetSetting(Setting, path);
                CheckResolution(shell, menu, GameShell.OnlineGameplayScenePath);
            }
            ProjectSettings.SetSetting(Setting, default);
            CheckResolution(shell, menu, GameShell.OnlineGameplayScenePath);
            GD.Print("ROUTING_PASS route=default_and_fallback");

            ProjectSettings.SetSetting(Setting, CustomScene);
            CheckResolution(shell, menu, CustomScene);
            await Route(() => menu.Call("new_game"), shell, OnlineGameplayLaunchKind.Host, null, "host");
            await Route(() => shell.JoinLobby("101"), shell, OnlineGameplayLaunchKind.Join, 101, "direct_join");
            await Route(() => Invite(shell, 202), shell, OnlineGameplayLaunchKind.Join, 202, "invite_join");

            // Exercise the real AppBootstrap +connect_lobby branch. The runner
            // launches this scene directly, with no --run flag to override it.
            string[] args = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray();
            int connect = Array.IndexOf(args, "+connect_lobby");
            Require(connect >= 0 && connect + 1 < args.Length && args[connect + 1] == "303",
                "Run this probe with +connect_lobby 303 and no --run flag.");
            await Route(() =>
            {
                AppBootstrap bootstrap = new();
                AddChild(bootstrap);
                bootstrap.QueueFree();
            }, shell, OnlineGameplayLaunchKind.Join, 303, "command_line_join");

            int before = _loads;
            shell.GameStartRequested();
            Invite(shell, 404);
            await Frames();
            Require(_loads == before, "Invite replaced a pending launch.");
            ConsumeOnce(shell, OnlineGameplayLaunchKind.Host, null);
            GD.Print("ROUTING_PASS route=invite_pending_rejection");
            result = 0;
        }
        catch (Exception exception) { GD.PushError($"ROUTING_FAIL: {exception}"); }
        finally
        {
            ProjectSettings.SetSetting(Setting, hadSetting ? original : default);
            loader.Disconnect("scene_loaded", loaded);
            GetTree().CurrentScene?.QueueFree();
            GetTree().CurrentScene = null;
            await Frames();
            GD.Print($"ROUTING_CLEANUP settings_restored=true loads={_loads}");
            GetTree().Quit(result);
        }
    }

    private async Task Route(Action request, GameShell shell, OnlineGameplayLaunchKind kind, ulong? lobby, string route)
    {
        Require(!shell.TryConsumeOnlineGameplayLaunchIntent(out _), "Previous route left an extra intent.");
        int before = _loads;
        ulong previousScene = GetTree().CurrentScene!.GetInstanceId();
        request();
        ulong deadline = Time.GetTicksMsec() + 5000;
        while (GetTree().CurrentScene is not Node current || current.GetInstanceId() == previousScene ||
            current.SceneFilePath != CustomScene || _loads == before)
        {
            Require(Time.GetTicksMsec() < deadline, $"{route} did not load the configured scene within 5 seconds.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await Frames();
        Require(_loads == before + 1, $"{route} loaded more than once.");
        ConsumeOnce(shell, kind, lobby);
        GD.Print($"ROUTING_PASS route={route} scene={CustomScene} kind={kind} lobby={lobby} loads=1 consumes=1");
    }

    private static void CheckResolution(GameShell shell, Node menu, string expected)
    {
        Require(shell.ResolveOnlineGameplayScenePath() == expected && menu.Call("get_game_scene_path").AsString() == expected,
            "Host and Join scene resolution differs.");
    }

    private static void ConsumeOnce(GameShell shell, OnlineGameplayLaunchKind kind, ulong? lobby)
    {
        Require(shell.TryConsumeOnlineGameplayLaunchIntent(out OnlineGameplayLaunchIntent intent), "Launch intent is missing.");
        Require(intent.Kind == kind && intent.LobbyId?.Value == lobby, "Launch intent kind or lobby changed.");
        Require(!shell.TryConsumeOnlineGameplayLaunchIntent(out _), "Launch intent was consumed twice.");
    }

    private static void Invite(GameShell shell, ulong lobby) =>
        typeof(GameShell).GetMethod("OnLobbyJoinRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(shell, new object[] { new SteamLobbyId(lobby), new SteamUserId(76561198000000001) });

    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
