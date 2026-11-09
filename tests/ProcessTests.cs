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
    static int HookHost(string mode, string root)
    {
        if (mode == "--hook-steam") { Thread.Sleep(15000); return 0; }
        if (mode == "--hook-host")
        {
            using (var child = Process.Start(new ProcessStartInfo(Path.Combine(root, "codex.exe"), "--hook-codex \"" + root + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(8000)) { child.Kill(); return 1; }
                Assert(output.Wait(1000) && error.Wait(1000), "Codex test process retained output handles");
                Console.Write(output.Result); Console.Error.Write(error.Result);
                return child.ExitCode;
            }
        }
        var input = Json.Serializer().Serialize(new {
            hook_event_name = "UserPromptSubmit", session_id = "stdin-session", turn_id = "stdin-turn",
            transcript_path = Path.Combine(root, "home", "sessions", "fixture.jsonl")
        });
        // Use the exact installed PowerShell command, including a Unicode/spaced path.
        string command = Hooks.Command(root);
        var store = new StateStore(root);
        try
        {
            using (var child = Process.Start(new ProcessStartInfo("powershell.exe", command.Substring("powershell.exe ".Length))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                  RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
                byte[] bytes = new UTF8Encoding(false).GetBytes(input);
                child.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); child.StandardInput.Close();
                if (!child.WaitForExit(2500)) { child.Kill(); throw new Exception("hook command timed out"); }
                Assert(output.Wait(500) && error.Wait(500), "worker retained hook output pipes");
                Assert(child.ExitCode == 0 && output.Result.Trim() == "{}", "hook command did not finish cleanly");
            }
            var state = store.Load();
            Assert(state.ActiveCount == 1 && state.Tasks[0].TurnId == "stdin-turn", "PowerShell lost hook stdin");
            if (!File.Exists(Path.Combine(root, "steam.exe")))
                Assert(state.Reason == "steam_missing" && state.WorkerPid == 0, "hook launched worker without Steam");
            else
            {
                Assert(Wait(() => store.Load().Phase == "waiting_login", 1500), "signed-out Steam did not create a bounded waiter");
                state = store.Load(); Assert(ProcessStamp.Alive(state.WorkerPid, state.WorkerTicks), "hook returned without a live worker");
                using (var worker = Worker(store))
                {
                    File.AppendAllText(Path.Combine(root, "home", "sessions", "fixture.jsonl"),
                        "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"stdin-turn\"}}\n", new UTF8Encoding(false));
                    Assert(worker.WaitForExit(2000), "worker did not retire completed hook task");
                }
            }
            return 0;
        }
        finally { Cleanup(store); }
    }
    static void HookInput(string source, string fixtures, bool runningSteam)
    {
        string root = Path.Combine(fixtures, runningSteam ? "通知管道 test" : "通知接收 test"); Directory.CreateDirectory(root);
        foreach (string name in new[] { "CodexSteamBridge.exe", "CodexSteamBridge.Hook.exe" }) File.Copy(Path.Combine(source, name), Path.Combine(root, name));
        string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
        File.Copy(self, Path.Combine(root, "Code.exe")); File.Copy(self, Path.Combine(root, "codex.exe"));
        string home = Path.Combine(root, "home"), sessions = Path.Combine(home, "sessions"); Directory.CreateDirectory(sessions);
        File.WriteAllText(Path.Combine(sessions, "fixture.jsonl"), "", new UTF8Encoding(false));
        Process steam = null;
        try
        {
            if (runningSteam)
            {
                File.Copy(self, Path.Combine(root, "steam.exe"));
                steam = Process.Start(new ProcessStartInfo(Path.Combine(root, "steam.exe"), "--hook-steam \"" + root + "\"")
                    { UseShellExecute = false, CreateNoWindow = true });
            }
            Json.Write(Path.Combine(root, "settings.json"), new Settings {
                SteamExe = Path.Combine(root, runningSteam ? "steam.exe" : "absent-steam.exe"), CodexHome = home });
            using (var host = Process.Start(new ProcessStartInfo(Path.Combine(root, "Code.exe"), "--hook-host \"" + root + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var output = host.StandardOutput.ReadToEndAsync(); var error = host.StandardError.ReadToEndAsync();
                if (!host.WaitForExit(10000)) { host.Kill(); throw new Exception("hook process chain timed out"); }
                Assert(error.Wait(1000) && output.Wait(1000), "hook test process retained output handles");
                Assert(host.ExitCode == 0, "registered PowerShell hook failed: " + error.Result);
            }
        }
        finally { if (steam != null) { if (!steam.HasExited) { steam.Kill(); steam.WaitForExit(2000); } steam.Dispose(); } }
        Console.WriteLine(runningSteam ? "PASS real process: worker releases notification pipes and exits on task completion" :
            "PASS real process: registered PowerShell command forwards task stdin through Unicode and spaced paths");
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && (args[0] == "--hook-host" || args[0] == "--hook-codex" || args[0] == "--hook-steam")) return HookHost(args[0], args[1]);
            var source = args[0]; string fixtures = args[1]; Directory.CreateDirectory(fixtures);
            HookInput(source, fixtures, false); HookInput(source, fixtures, true);
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
