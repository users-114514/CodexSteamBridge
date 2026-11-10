using CodexSteamBridge;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public static class CliTests
{
    static string root, app;
    static int count;
    static void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
    static void Case(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
    static bool Wait(Func<bool> condition, int timeout)
    { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < timeout) { if (condition()) return true; Thread.Sleep(40); } return condition(); }
    static Dictionary<string, object> Input(string id, string turn, string path, string name)
    { return new Dictionary<string, object> { { "session_id", id }, { "turn_id", turn }, { "transcript_path", path }, { "hook_event_name", name } }; }
    static void Append(string path, string kind, string turn, string trigger)
    {
        var payload = new Dictionary<string, object> { { "type", kind }, { "turn_id", turn } };
        if (trigger != null) payload["turn_attribution"] = new { turn_trigger = trigger };
        File.AppendAllText(path, Json.Serializer().Serialize(new { type = "event_msg", payload = payload }) + "\n", new UTF8Encoding(false));
    }
    static string Transcript(string home, string name, string source, string turn, string trigger)
    {
        string path = Path.Combine(home, "sessions", name + ".jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, Json.Serializer().Serialize(new { type = "session_meta", payload = new { source = source } }) + "\n", new UTF8Encoding(false));
        if (turn != null) Append(path, "task_started", turn, trigger);
        return path;
    }
    static CodexProfile Profile(string id, string home, string exe)
    { Directory.CreateDirectory(home); return new CodexProfile { Id = id, CodexHome = home, CliEntry = exe, CliEnabled = true }; }
    static void Kill(int pid, long ticks)
    { if (ProcessStamp.Alive(pid, ticks)) using (var p = Process.GetProcessById(pid)) { p.Kill(); p.WaitForExit(2000); } }

    static int Fixture(string[] args)
    {
        string folder = args[args.Length - 2], id = args[args.Length - 1];
        if (args[0] == "--steam") { Thread.Sleep(30000); return 0; }
        if (args[0] == "--terminal")
        {
            using (Process.Start(new ProcessStartInfo(Path.Combine(folder, "codex.exe"), "app-server --fixture \"" + folder + "\" " + id)
                { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
            return 0;
        }
        var self = ProcessStamp.Current();
        Json.Write(Path.Combine(folder, id + "-owner.json"), self);
        string path = Path.Combine(folder, id, "sessions", "turn.jsonl");
        string command = Hooks.Command(folder, id);
        using (var hook = Process.Start(new ProcessStartInfo("powershell.exe", command.Substring("powershell.exe ".Length)) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            var clock = Stopwatch.StartNew(); var output = hook.StandardOutput.ReadToEndAsync(); var error = hook.StandardError.ReadToEndAsync();
            byte[] input = Encoding.UTF8.GetBytes(Json.Serializer().Serialize(Input("same-session", "same-turn", path, "UserPromptSubmit")));
            hook.StandardInput.BaseStream.Write(input, 0, input.Length); hook.StandardInput.Close();
            Assert(hook.WaitForExit(2800) && output.Wait(150) && error.Wait(150), "CLI hook exceeded three-second deadline or inherited pipes");
            Assert(hook.ExitCode == 0 && output.Result.Trim() == "{}" && clock.ElapsedMilliseconds < 3000, "invalid CLI hook output");
        }
        File.WriteAllText(Path.Combine(folder, id + "-returned"), "");
        Thread.Sleep(30000); return 0;
    }

    static void ProcessScenarios()
    {
        string folder = Path.Combine(root, "CLI 进程 中文"); Directory.CreateDirectory(folder);
        foreach (string name in new[] { "CodexSteamBridge.exe", "CodexSteamBridge.Hook.exe" }) File.Copy(Path.Combine(app, name), Path.Combine(folder, name));
        string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
        foreach (string name in new[] { "codex.exe", "terminal.exe", "steam.exe" }) File.Copy(executable, Path.Combine(folder, name));
        var settings = new Settings { SteamExe = Path.Combine(folder, "steam.exe"), DefaultProfileId = "one" };
        foreach (string id in new[] { "one", "two" })
        {
            var p = Profile(id, Path.Combine(folder, id), Path.Combine(folder, "codex.exe")); settings.CodexProfiles.Add(p);
            Transcript(p.CodexHome, "turn", "cli", "same-turn", "user");
        }
        var store = new StateStore(folder); Json.Write(store.SettingsPath, settings);
        using (var steam = Process.Start(new ProcessStartInfo(settings.SteamExe, "--steam \"" + folder + "\" one") { UseShellExecute = false, CreateNoWindow = true }))
        {
            try
            {
                using (var terminal = Process.Start(new ProcessStartInfo(Path.Combine(folder, "terminal.exe"), "--terminal \"" + folder + "\" one") { UseShellExecute = false, CreateNoWindow = true }))
                    Assert(terminal.WaitForExit(1500), "terminal fixture did not detach");
                Assert(Wait(() => File.Exists(Path.Combine(folder, "one-returned")), 5000), "shared daemon hook failed after terminal exited");
                var first = store.Load(); var owner = Json.Read<ProcessStamp>(Path.Combine(folder, "one-owner.json"));
                Assert(first.ActiveCount == 1 && first.Tasks[0].Client == "cli" && first.Tasks[0].OwnerPid == owner.Pid && ProcessStamp.Alive(first.WorkerPid, first.WorkerTicks), "daemon not identified as task owner");
                using (var direct = Process.Start(new ProcessStartInfo(Path.Combine(folder, "codex.exe"), "--no-daemon --fixture \"" + folder + "\" two") { UseShellExecute = false, CreateNoWindow = true }))
                {
                    Assert(Wait(() => File.Exists(Path.Combine(folder, "two-returned")), 5000), "no-daemon hook failed");
                    var both = store.Load(); Assert(both.ActiveCount == 2 && both.WorkerPid == first.WorkerPid && both.Epoch == first.Epoch, "profiles did not share one worker or collided on session id");
                    Kill(owner.Pid, owner.Ticks);
                    Assert(Wait(() => store.Load().ActiveCount == 1, 1800), "daemon crash did not retire only its own task");
                    var clock = Stopwatch.StartNew(); Append(Path.Combine(folder, "two", "sessions", "turn.jsonl"), "task_complete", "same-turn", null);
                    Assert(Wait(() => !ProcessStamp.Alive(first.WorkerPid, first.WorkerTicks), 2000), "last task completion exceeded two seconds");
                    Assert(clock.ElapsedMilliseconds < 2000, "completion deadline exceeded");
                    direct.Kill(); direct.WaitForExit(2000);
                }
                Console.WriteLine("PASS CLI processes: detached daemon, no-daemon, two homes, UTF-8 command, pipe release, crash isolation and <2s exit");
            }
            finally
            {
                foreach (string id in new[] { "one", "two" }) if (File.Exists(Path.Combine(folder, id + "-owner.json"))) { var owner = Json.Read<ProcessStamp>(Path.Combine(folder, id + "-owner.json")); Kill(owner.Pid, owner.Ticks); }
                var state = store.Load(); Kill(state.WorkerPid, state.WorkerTicks); Kill(state.PresencePid, state.PresenceTicks);
                if (!steam.HasExited) { steam.Kill(); steam.WaitForExit(2000); }
            }
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length >= 3 && (args[0] == "--steam" || args[0] == "--terminal" || args.Contains("--fixture"))) return Fixture(args);
            app = args[0]; root = args[1]; Directory.CreateDirectory(root);
            Case("schema2 migrates once and preserves legacy hook command", () => {
                var s = new Settings { SchemaVersion = 2, CodexHome = root, CodexExe = "old.exe" }; Profiles.Normalize(s);
                Assert(s.SchemaVersion == 3 && s.CodexProfiles.Count == 1 && s.CodexProfiles[0].LegacyHooks, "legacy migration lost");
                s.CodexProfiles.Clear(); Profiles.Normalize(s); Assert(s.CodexProfiles.Count == 0, "removed final profile was recreated");
            });
            Case("Windows paths deduplicate including case, dots and trailing separators", () => {
                var s = new Settings(); s.CodexProfiles.Add(Profile("one", root, app + "\\CodexSteamBridge.exe"));
                s.CodexProfiles.Add(Profile("two", root.ToUpperInvariant() + "\\.\\", app + "\\CodexSteamBridge.exe"));
                bool failed = false; try { Profiles.Validate(s); } catch (InvalidDataException) { failed = true; } Assert(failed, "duplicate home accepted");
            });
            Case("npm and NVM launcher re-resolves after native update", () => {
                string folder = Path.Combine(root, "npm 中文 路径"), package = Path.Combine(folder, "node_modules", "@openai", "codex"); Directory.CreateDirectory(package);
                string entry = Path.Combine(folder, "codex.cmd"); File.WriteAllText(entry, "@echo off\n"); File.WriteAllText(Path.Combine(package, "package.json"), "{\"name\":\"@openai/codex\"}");
                string binary = Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"); Directory.CreateDirectory(Path.GetDirectoryName(binary)); File.WriteAllText(binary, "v1");
                Assert(CliDiscovery.Resolve(entry) == binary, "npm native binary not resolved"); File.Delete(binary); Assert(CliDiscovery.Resolve(entry) == "", "removed binary cached");
                binary = Path.Combine(package, "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"); Directory.CreateDirectory(Path.GetDirectoryName(binary)); File.WriteAllText(binary, "v2");
                Assert(CliDiscovery.Resolve(entry) == binary, "updated vendor binary not resolved");
            });
            Case("interactive commands accepted, exec/review/remote rejected", () => {
                foreach (string[] a in new[] { new string[0], new[] { "resume", "--last" }, new[] { "fork", "--last" }, new[] { "--no-daemon", "hello" }, new[] { "app-server", "--stdio" }, new[] { "-c", "x=1", "hello" } }) Assert(Native.SupportedInvocation(a), "interactive command rejected");
                foreach (string[] a in new[] { new[] { "exec", "hello" }, new[] { "-c", "x=1", "review" }, new[] { "--remote=ws://host", "hello" }, new[] { "resume", "--remote", "ws://host" } }) Assert(!Native.SupportedInvocation(a), "unsupported command accepted");
                using (var p = Process.GetCurrentProcess()) Assert(Native.Arguments(p).Length == 2, "actual command line not decoded");
            });
            Case("client-specific trigger allowlist excludes continuation and children", () => {
                foreach (string client in new[] { "cli", "vscode" }) foreach (string trigger in new[] { "user", "composer", "edit_user_message", "plan_implementation", "resume_interrupted_task", "hook", "agent" })
                {
                    var t = new TaskInfo { Client = client, RootId = "t", TurnId = "t", Trigger = trigger };
                    Assert(TranscriptAdapter.UserTask(t) == (client == "cli" ? trigger == "user" : new[] { "composer", "edit_user_message", "plan_implementation" }.Contains(trigger)), "trigger misclassified");
                    t.RootId = "parent"; Assert(!TranscriptAdapter.UserTask(t), "child became user submission");
                }
            });
            Case("same session and turn in two profiles remain independent", () => {
                var s = new BridgeState(); var owner = ProcessStamp.Current(); owner.Client = "cli";
                foreach (string id in new[] { "one", "two" }) Assert(Notifications.Apply(s, Input(Profiles.SessionKey(id, "s"), "t", "fixture", "UserPromptSubmit"), owner, p => true, p => 0), "profile collided");
                Notifications.Apply(s, Input("one:s", "t", "fixture", "Interrupt"), owner, p => true, p => 0); Assert(s.ActiveCount == 1 && s.Tasks[1].Active, "cancel crossed profiles");
            });
            Case("CLI SessionEnd is advisory while actual daemon remains alive", () => {
                var s = new BridgeState(); var owner = ProcessStamp.Current(); owner.Client = "cli";
                Notifications.Apply(s, Input("s", "t", "fixture", "UserPromptSubmit"), owner, p => true, p => 0);
                Notifications.Apply(s, Input("s", "", "fixture", "SessionEnd"), owner, p => true, p => 0); Assert(s.ActiveCount == 1, "terminal closure ended daemon work");
                var other = new ProcessStamp { Pid = owner.Pid, Ticks = owner.Ticks + 1, Client = "vscode" };
                Notifications.Apply(s, Input("s", "t", "fixture", "Interrupt"), other, p => true, p => 0); Assert(s.ActiveCount == 1, "old owner cancellation killed resumed task");
            });
            Case("format errors isolate a session and task-start waiting has a deadline", () => {
                var state = new BridgeState(); var owner = ProcessStamp.Current(); owner.Client = "cli";
                foreach (string id in new[] { "bad", "good" })
                {
                    string path = Transcript(root, id, "cli", "t", "user"); Notifications.Apply(state, Input(id, "t", path, "UserPromptSubmit"), owner, p => true, p => 0);
                    state.Sessions.Last().ProfileId = id;
                }
                Append(state.Sessions[0].Transcript, "task_future", "t", null); TranscriptAdapter.Reconcile(state);
                Assert(state.ActiveCount == 1 && state.Tasks.Single(t => t.Active).SessionId == "good" && state.Issues.Single().ProfileId == "bad", "format error ended unrelated work");
                var good = state.Tasks.Single(t => t.Active); good.Trigger = ""; good.AwaitStartUntil = DateTime.UtcNow.AddSeconds(2).Ticks;
                TranscriptAdapter.Reconcile(state); Assert(good.Active, "pending start ended early"); good.AwaitStartUntil = DateTime.UtcNow.AddSeconds(-1).Ticks;
                TranscriptAdapter.Reconcile(state); Assert(!good.Active, "unknown start stayed active");
            });
            Case("cross-client resume reads old and new records with independent owners", () => {
                var state = new BridgeState(); var owner = ProcessStamp.Current(); owner.Client = "cli";
                string old = Transcript(root, "old", "cli", "a", "user"), fresh = Transcript(root, "new", "vscode", "b", "composer");
                Notifications.Apply(state, Input("s", "a", old, "UserPromptSubmit"), owner, p => true, p => 0); TranscriptAdapter.Reconcile(state);
                owner = ProcessStamp.Current(); owner.Client = "vscode";
                Notifications.Apply(state, Input("s", "b", fresh, "UserPromptSubmit"), owner, p => true, p => 0);
                Append(old, "task_complete", "a", null); TranscriptAdapter.Reconcile(state);
                Assert(state.ActiveCount == 1 && state.Tasks.Single(t => t.Active).TurnId == "b", "old completion ended new client task");
                Append(fresh, "turn_aborted", "b", null); TranscriptAdapter.Reconcile(state); Assert(state.ActiveCount == 0, "resumed client cancellation lost");
            });
            Case("CLI-only multi-home install, rollback, migration and owned uninstall", () => {
                string target = Path.Combine(root, "安装 target"), home1 = Path.Combine(root, "配置 1"), home2 = Path.Combine(root, "配置 2"), steam = Path.Combine(root, "steam.exe"); File.WriteAllText(steam, "fixture");
                var s = new Settings { SteamExe = steam }; s.CodexProfiles.Add(Profile("one", home1, Path.Combine(app, "CodexSteamBridge.exe"))); s.CodexProfiles.Add(Profile("two", home2, Path.Combine(app, "CodexSteamBridge.exe")));
                Installer.Install(app, target, s, new[] { "123" });
                byte[] hooks1 = File.ReadAllBytes(Path.Combine(home1, "hooks.json")), hooks2 = File.ReadAllBytes(Path.Combine(home2, "hooks.json"));
                Assert(Hooks.Keys(hooks1, Path.Combine(home1, "hooks.json"), target).Count == 7 && Encoding.UTF8.GetString(hooks1).Contains("--profile one"), "wrong home hooks");
                File.WriteAllText(Path.Combine(home2, "hooks.json"), "invalid");
                var saved = File.ReadAllBytes(Path.Combine(target, "settings.json")); s.CodexProfiles[0].Id = "changed";
                bool failed = false; try { Installer.Install(app, target, s, new[] { "123" }); } catch (ArgumentException) { failed = true; }
                Assert(failed && hooks1.SequenceEqual(File.ReadAllBytes(Path.Combine(home1, "hooks.json"))) && saved.SequenceEqual(File.ReadAllBytes(Path.Combine(target, "settings.json"))), "partial profile install not rolled back");
                File.WriteAllBytes(Path.Combine(home2, "hooks.json"), hooks2); s = new StateStore(target).Settings(); s.CodexProfiles.RemoveAt(1);
                Installer.Install(app, target, s, new[] { "123" }); Assert(!File.ReadAllText(Path.Combine(home2, "hooks.json")).Contains("CodexSteamBridge"), "removed profile left hooks");
                string config = Path.Combine(home1, "config.toml"); string key = Hooks.Keys(hooks1, Path.Combine(home1, "hooks.json"), target)[0];
                File.WriteAllText(config, "[hooks.state." + Json.Serializer().Serialize(key) + "]\ntrusted_hash='owned'\n[hooks.state.'other']\ntrusted_hash='keep'\n");
                Installer.Uninstall(target, false); Assert(!File.ReadAllText(config).Contains("'owned'") && File.ReadAllText(config).Contains("'keep'"), "trust cleanup crossed ownership");
            });
            Case("manager constructs without startup error", () => { using (var manager = new Manager()) Assert(manager.Controls.Count > 0, "manager controls absent"); });
            Case("upgrade preserves legacy command and trust bytes; replay does not revive stopped work", () => {
                string target = Path.Combine(root, "legacy install"), home = Path.Combine(root, "legacy home"), exe = Path.Combine(app, "CodexSteamBridge.exe");
                var settings = new Settings { SchemaVersion = 2, CodexHome = home, CodexExe = exe, SteamExe = exe }; Directory.CreateDirectory(home);
                Installer.Install(app, target, settings, new[] { "123" }); byte[] hooks = File.ReadAllBytes(Path.Combine(home, "hooks.json"));
                string config = Path.Combine(home, "config.toml"); File.WriteAllText(config, "[hooks.state.'untouched']\ntrusted_hash='keep'\n"); byte[] trust = File.ReadAllBytes(config);
                settings = new StateStore(target).Settings(); settings.CodexProfiles[0].CliEnabled = true; settings.CodexProfiles[0].CliEntry = exe;
                Installer.Install(app, target, settings, new[] { "123" });
                Assert(hooks.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "hooks.json"))) && trust.SequenceEqual(File.ReadAllBytes(config)), "upgrade changed existing review");
                Assert(new StateStore(target).Load().Reason == "waiting_new_task", "upgrade restored old work");
                var state = new BridgeState(); var owner = ProcessStamp.Current(); owner.Client = "cli";
                var input = Input("s", "t", "fixture", "UserPromptSubmit"); Assert(Notifications.Apply(state, input, owner, p => true, p => 0), "new submission lost");
                Lifecycle.Stop(state, "presence_stopped"); Assert(!Notifications.Apply(state, input, owner, p => true, p => 0) && state.Phase == "stopped", "duplicate revived manual stop");
            });
            ProcessScenarios();
            Console.WriteLine("RESULT " + count + " CLI/profile groups passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
