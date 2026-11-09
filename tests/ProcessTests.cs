using CodexSteamBridge;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

public static class ProcessTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Wait(Func<bool> condition, int milliseconds)
    { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { if (condition()) return true; Thread.Sleep(50); } return condition(); }
    static StateStore Prepare(string source, string root, Settings settings)
    {
        Directory.CreateDirectory(root);
        foreach (string name in new[] { "CodexSteamBridge.exe", "CodexSteamBridge.Hook.exe" }) File.Copy(Path.Combine(source, name), Path.Combine(root, name));
        var store = new StateStore(root); Json.Write(store.SettingsPath, settings);
        var self = ProcessStamp.Current();
        string transcript = Path.Combine(root, "fixture.jsonl"); File.WriteAllText(transcript, "", new UTF8Encoding(false));
        var state = new BridgeState { Desired = true, Phase = "active" };
        state.Sessions.Add(new SessionInfo { Id = "s", OwnerPid = self.Pid, OwnerTicks = self.Ticks, Transcript = transcript });
        state.Tasks.Add(new TaskInfo { SessionId = "s", TurnId = "t", RootId = "t" }); store.Save(state);
        return store;
    }
    static Process Presence(StateStore store, string game)
    {
        var start = new ProcessStartInfo(Path.Combine(store.Root, "CodexSteamBridge.exe"), "presence") { UseShellExecute = false, CreateNoWindow = true };
        start.EnvironmentVariables["SteamGameId"] = game;
        return Process.Start(start);
    }
    static void End(StateStore store)
    {
        using (var writer = new StreamWriter(new FileStream(Path.Combine(store.Root, "fixture.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)))
            writer.WriteLine("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"t\",\"root_turn_id\":\"t\"}}");
    }
    static Process Worker(StateStore store)
    { var state = store.Load(); return Process.GetProcessById(state.WorkerPid); }
    static void Cleanup(StateStore store)
    {
        var state = store.Load();
        foreach (var pair in new[] { new ProcessStamp { Pid = state.WorkerPid, Ticks = state.WorkerTicks }, new ProcessStamp { Pid = state.PresencePid, Ticks = state.PresenceTicks } })
            if (ProcessStamp.Alive(pair.Pid, pair.Ticks)) using (var process = Process.GetProcessById(pair.Pid)) { process.Kill(); process.WaitForExit(2000); }
    }
    public static int Main(string[] args)
    {
        try
        {
            var source = args[0]; string fixtures = args[1]; Directory.CreateDirectory(fixtures);
            var missing = Prepare(source, Path.Combine(fixtures, "missing"), new Settings { SteamExe = Path.Combine(fixtures, "absent-steam.exe") });
            using (var presence = Presence(missing, "1")) Assert(presence.WaitForExit(2000), "missing Steam kept presence alive");
            var state = missing.Load(); Assert(state.WorkerPid == 0, "missing Steam started worker");
            Console.WriteLine("PASS real process: missing Steam exits without worker");
            var native = Discovery.Detect(); var steam = SteamPlatform.Inspect(native);
            if (!steam.Running || steam.AccountId.Length == 0) { Console.WriteLine("SKIP live-client isolated process checks: Steam not logged in"); return 0; }
            native.Accounts.Add(new AccountConfig { AccountId = steam.AccountId, GameId = "123456789" });
            foreach (string scenario in new[] { "completion", "manual-stop", "worker-crash" })
            {
                var store = Prepare(source, Path.Combine(fixtures, scenario), native);
                try
                {
                    using (var presence = Presence(store, "123456789"))
                    {
                        Assert(Wait(() => { try { var s = store.Load(); return s.Phase == "active" && s.WorkerPid > 0 && s.HeartbeatTicks > 0 && ProcessStamp.Alive(s.WorkerPid, s.WorkerTicks); } catch { return false; } }, 3000), "isolated worker did not start");
                        using (var worker = Worker(store))
                        {
                            var clock = Stopwatch.StartNew();
                            if (scenario == "completion")
                            {
                                End(store); Assert(presence.WaitForExit(2000) && worker.WaitForExit(Math.Max(1, 2000 - (int)clock.ElapsedMilliseconds)), "completion exceeded two seconds");
                                Console.WriteLine("PASS real process: completion exit " + clock.ElapsedMilliseconds + "ms");
                            }
                            else if (scenario == "manual-stop")
                            {
                                presence.Kill(); presence.WaitForExit(2000); Assert(worker.WaitForExit(2000), "stopped presence relaunched or worker remained");
                                Assert(store.Load().Reason == "presence_stopped", "stop suppression absent");
                                Console.WriteLine("PASS real process: manual stop exits worker without relaunch");
                            }
                            else
                            {
                                worker.Kill(); worker.WaitForExit(2000); Assert(presence.WaitForExit(5000), "orphan presence exceeded heartbeat budget");
                                Console.WriteLine("PASS real process: orphan presence exit " + clock.ElapsedMilliseconds + "ms");
                            }
                        }
                    }
                }
                finally { Cleanup(store); }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
