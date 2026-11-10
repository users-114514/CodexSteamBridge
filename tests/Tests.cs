using CodexSteamBridge;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

public static class Tests
{
    static int count;
    static string root;
    static long now = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc).Ticks;
    static Settings Settings() { return new Settings(); }
    static SteamObservation Steam() { return new SteamObservation { Installed = true, Running = true, Pid = 4, StartTicks = 5, AccountId = "123", Configured = true }; }
    static BridgeState Work() { var s = new BridgeState(); s.Tasks.Add(new TaskInfo { SessionId = "s", TurnId = "t", RootId = "t" }); return s; }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Case(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
    static Dictionary<string, object> Event(string type, string turn)
    { return new Dictionary<string, object> { { "hook_event_name", type }, { "session_id", "s" }, { "turn_id", turn }, { "transcript_path", "fixture" } }; }
    static List<object> AsList(object value) { return ((IEnumerable)value).Cast<object>().ToList(); }

    public static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        try
        {
            Case("missing Steam exits without launch or retry", () => {
                var s = Work(); var steam = Steam(); steam.Installed = false;
                Lifecycle.Begin(s, steam, now, false);
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Exit && !s.LaunchRequested, "missing Steam retained a worker");
                steam.Installed = true;
                Assert(Lifecycle.Tick(s, Settings(), steam, now + TimeSpan.FromSeconds(20).Ticks, false) == NextAction.Exit, "same task retried");
            });
            Case("installed but stopped Steam never launches", () => {
                var s = Work(); var steam = Steam(); steam.Running = false; Lifecycle.Begin(s, steam, now, false);
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Exit && !s.LaunchRequested, "started stopped client");
            });
            Case("new task resets a suppressed epoch", () => {
                var s = Work(); var steam = Steam(); steam.Installed = false; Lifecycle.Begin(s, steam, now, false);
                steam.Installed = true; Lifecycle.Begin(s, steam, now, false);
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Launch, "new epoch not allowed");
            });
            Case("one silent launch and exact 10 second deadline", () => {
                var s = Work(); var steam = Steam(); Lifecycle.Begin(s, steam, now, false);
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Launch, "initial launch missing");
                Assert(Lifecycle.Tick(s, Settings(), steam, now + TimeSpan.FromSeconds(9).Ticks, false) == NextAction.Wait, "premature timeout");
                Assert(Lifecycle.Tick(s, Settings(), steam, now + TimeSpan.FromSeconds(10).Ticks, false) == NextAction.Exit, "deadline missing");
            });
            Case("60 second login deadline", () => {
                var s = Work(); var steam = Steam(); steam.AccountId = ""; steam.Configured = false; Lifecycle.Begin(s, steam, now, false);
                Assert(Lifecycle.Tick(s, Settings(), steam, now + TimeSpan.FromSeconds(59).Ticks, false) == NextAction.Wait, "login wait missing");
                Assert(Lifecycle.Tick(s, Settings(), steam, now + TimeSpan.FromSeconds(60).Ticks, false) == NextAction.Exit, "login wait unbounded");
            });
            Case("login at 59 seconds launches once", () => {
                var s = Work(); var steam = Steam(); steam.AccountId = ""; Lifecycle.Begin(s, steam, now, false); steam.AccountId = "123";
                Assert(Lifecycle.Tick(s, Settings(), steam, now + TimeSpan.FromSeconds(59).Ticks, false) == NextAction.Launch, "login not picked up");
            });
            Case("task ends while waiting for login", () => {
                var s = Work(); var steam = Steam(); steam.AccountId = ""; Lifecycle.Begin(s, steam, now, false); s.Tasks.Clear();
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Exit, "idle login waiter remained");
            });
            Case("unconfigured account exits", () => {
                var s = Work(); var steam = Steam(); steam.Configured = false; Lifecycle.Begin(s, steam, now, false);
                Assert(s.Reason == "account_not_configured" && Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Exit, "account not gated");
            });
            Case("presence stop suppresses current task", () => {
                var s = Work(); var steam = Steam(); Lifecycle.Begin(s, steam, now, false);
                Lifecycle.Tick(s, Settings(), steam, now, false); Lifecycle.Tick(s, Settings(), steam, now, true);
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Exit, "stopped presence restarted");
                Assert(Lifecycle.Tick(s, Settings(), steam, now, false) == NextAction.Exit, "suppression lost");
            });
            Case("Steam exit, restart and account switch end epoch", () => {
                foreach (int variation in new[] { 0, 1, 2, 3 }) {
                    var s = Work(); var steam = Steam(); Lifecycle.Begin(s, steam, now, true);
                    if (variation == 0) steam.Running = false;
                    if (variation == 1) steam.StartTicks++;
                    if (variation == 2) steam.AccountId = "456";
                    if (variation == 3) steam.AccountId = "";
                    Assert(Lifecycle.Tick(s, Settings(), steam, now, true) == NextAction.Exit, "client transition ignored");
                }
            });
            Case("manual Play resumes work and idle Play exits", () => {
                var s = Work(); var steam = Steam(); Lifecycle.Stop(s, "presence_stopped"); Lifecycle.Begin(s, steam, now, true);
                Assert(Lifecycle.Tick(s, Settings(), steam, now, true) == NextAction.Wait && s.Desired, "manual resume failed");
                s.Tasks.Clear(); Lifecycle.Begin(s, steam, now, true); Assert(s.Phase == "stopped", "idle manual Play persisted");
            });
            Case("all windows and children must finish", () => {
                var s = Work(); s.Tasks.Add(new TaskInfo { SessionId = "second", TurnId = "u" }); s.Agents.Add(new AgentInfo { Id = "child", Active = true });
                var steam = Steam(); Lifecycle.Begin(s, steam, now, true); s.Tasks[0].Active = false; s.Tasks[1].Active = false;
                Assert(Lifecycle.Tick(s, Settings(), steam, now, true) == NextAction.Wait, "child not counted");
                s.Agents[0].Active = false; Assert(Lifecycle.Tick(s, Settings(), steam, now, true) == NextAction.Exit, "finished work remained");
            });
            Case("disabled integration exits", () => {
                var s = Work(); var steam = Steam(); Lifecycle.Begin(s, steam, now, true); var settings = Settings(); settings.Enabled = false;
                Assert(Lifecycle.Tick(s, settings, steam, now, true) == NextAction.Exit, "disable ignored");
            });
            Case("notification deduplication and continuation", () => {
                var s = new BridgeState(); var owner = new ProcessStamp { Pid = 1, Ticks = 2 };
                Func<Dictionary<string, object>, bool> send = e => Notifications.Apply(s, e, owner, p => true, p => 0);
                Assert(send(Event("UserPromptSubmit", "t")), "new task missing"); Assert(!send(Event("UserPromptSubmit", "t")), "duplicate launched");
                var continuation = Event("UserPromptSubmit", "continued"); continuation["root_turn_id"] = "t";
                Assert(!send(continuation), "continuation launched"); Assert(!send(Event("Stop", "t")) && s.ActiveCount == 1, "Stop prematurely ended task");
                Assert(!send(Event("Interrupt", "t")) && s.ActiveCount == 0, "cancel did not end task");
            });
            Case("panel session does not become task; unowned process ignored", () => {
                var s = new BridgeState(); Assert(!Notifications.Apply(s, Event("SessionStart", ""), new ProcessStamp { Pid = 1, Ticks = 2 }, p => true, p => 0) && s.ActiveCount == 0, "panel launched");
                Assert(!Notifications.Apply(new BridgeState(), Event("UserPromptSubmit", "t"), null, p => true, p => 0), "CLI accepted");
            });
            Case("root completion and unknown task format", () => {
                var s = Work(); TranscriptAdapter.Apply(s, "s", new Dictionary<string, object> { { "type", "task_started" }, { "turn_id", "alias" }, { "root_turn_id", "t" } });
                TranscriptAdapter.Apply(s, "s", new Dictionary<string, object> { { "type", "task_complete" }, { "turn_id", "alias" } }); Assert(s.ActiveCount == 0, "alias completion ignored");
                bool threw = false; try { TranscriptAdapter.Apply(s, "s", new Dictionary<string, object> { { "type", "task_future" } }); } catch (InvalidDataException) { threw = true; } Assert(threw, "unknown schema silently accepted");
            });
            Case("turn attribution distinguishes automatic continuation", () => {
                var s = Work();
                TranscriptAdapter.Apply(s, "s", new Dictionary<string, object> { { "type", "task_started" }, { "turn_id", "t" }, { "root_turn_id", "earlier" },
                    { "turn_attribution", new Dictionary<string, object> { { "turn_trigger", "hook" }, { "root_turn_id", "earlier" } } } });
                Assert(s.Tasks[0].Trigger == "hook" && s.Tasks[0].RootId == "earlier", "continuation attribution lost");
            });
            Case("partial transcript is not consumed until newline", () => {
                string path = Path.Combine(root, "partial.jsonl"); string line = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"t\"}}";
                File.WriteAllText(path, line, new UTF8Encoding(false)); int events = 0;
                Assert(TranscriptAdapter.Read(path, 0, p => events++) == 0 && events == 0, "partial record consumed"); File.AppendAllText(path, "\n");
                Assert(TranscriptAdapter.Read(path, 0, p => events++) == new FileInfo(path).Length && events == 1, "complete record lost");
            });
            Case("long Windows paths and traversal rejection", () => {
                string home = Path.Combine(root, "用户 home"); string folder = Path.Combine(home, "sessions"); Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, "old.jsonl"); File.WriteAllText(file, "");
                Assert(TranscriptAdapter.ValidPath(@"\\?\" + file, home), "extended path rejected"); Assert(!TranscriptAdapter.ValidPath(Path.Combine(root, "partial.jsonl"), home), "outside transcript accepted");
            });
            Case("atomic state and concurrent locking", () => {
                var store = new StateStore(Path.Combine(root, "状态 concurrent")); store.Save(new BridgeState());
                var threads = Enumerable.Range(0, 8).Select(i => new Thread(() => { for (int n = 0; n < 10; n++) { var mutex = store.Lock(); try { var s = store.Load(); s.Epoch++; store.Save(s); } finally { StateStore.Unlock(mutex); } } })).ToArray();
                foreach (var thread in threads) thread.Start(); foreach (var thread in threads) thread.Join(); Assert(store.Load().Epoch == 80, "lost update");
            });
            Case("state excludes prompt and body", () => {
                var s = new BridgeState(); var input = Event("UserPromptSubmit", "t"); input["prompt"] = "PRIVATE_SENTINEL";
                Notifications.Apply(s, input, new ProcessStamp { Pid = 1, Ticks = 2 }, p => true, p => 0);
                Assert(!Json.Serializer().Serialize(s).Contains("PRIVATE_SENTINEL"), "prompt persisted");
            });
            Case("hook merge idempotent and unrelated handler preserved", () => {
                byte[] original = Encoding.UTF8.GetBytes("{\"description\":\"keep\",\"hooks\":{\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"existing.exe\"}]}]}}");
                byte[] once = Hooks.Merge(original, root, false), twice = Hooks.Merge(once, root, false);
                Assert(once.SequenceEqual(twice), "duplicate hooks added"); var result = Encoding.UTF8.GetString(Hooks.Merge(twice, root, true));
                Assert(result.Contains("existing.exe") && !result.Contains("CodexSteamBridge.Hook.exe") && result.Contains("keep"), "unrelated hook removed");
            });
            Case("trust cleanup preserves other sections", () => {
                byte[] before = Encoding.UTF8.GetBytes("model = 'keep'\n[hooks.state.'owned']\ntrusted_hash = 'secret'\n[hooks.state.'other']\ntrusted_hash = 'keep'\n");
                string after = Encoding.UTF8.GetString(Hooks.RemoveTrust(before, new[] { "owned" })); Assert(!after.Contains("secret") && after.Contains("other") && after.Contains("model"), "trust cleanup damaged other config");
            });
            Case("VDF roundtrip preserves unknown fields and other games", () => {
                var tree = new List<VdfEntry> { new VdfEntry(0, "shortcuts", new List<VdfEntry> { new VdfEntry(0, "0", new List<VdfEntry> { new VdfEntry(1, "Exe", "other.exe"), new VdfEntry(2, "appid", (uint)2), new VdfEntry(1, "future_field", "keep") }) }) };
                byte[] original = Vdf.Write(tree); Assert(Vdf.Write(Vdf.Read(original)).SequenceEqual(original), "roundtrip changed bytes");
                var account = Vdf.EnsureShortcut(tree, root, "123", "icon"); byte[] once = Vdf.Write(tree); var again = Vdf.EnsureShortcut(tree, root, "123", "new icon");
                Assert(once.SequenceEqual(Vdf.Write(tree)) && account.GameId == again.GameId, "repair changed shortcut");
                Vdf.RemoveShortcut(tree, root, new[] { account }); Assert(original.SequenceEqual(Vdf.Write(tree)), "other game changed");
            });
            Case("transaction rollback and commit", () => {
                string path = Path.Combine(root, "rollback.txt"), fresh = Path.Combine(root, "fresh.txt"); File.WriteAllText(path, "old");
                using (var transaction = new FileTransaction()) { transaction.Write(path, Encoding.UTF8.GetBytes("new")); transaction.Write(fresh, new byte[] { 1 }); }
                Assert(File.ReadAllText(path) == "old" && !File.Exists(fresh), "rollback failed");
                using (var transaction = new FileTransaction()) { transaction.Write(path, Encoding.UTF8.GetBytes("committed")); transaction.Commit(); }
                Assert(File.ReadAllText(path) == "committed", "commit failed");
            });
            Case("isolated install repeated repair and multiple accounts", () => {
                string fixture = Path.Combine(root, "安装 fixture"), steam = Path.Combine(fixture, "steam.exe"), home = Path.Combine(fixture, "codex home"), target = Path.Combine(root, "安装 with spaces");
                Directory.CreateDirectory(home); File.WriteAllText(steam, "fixture");
                var settings = new Settings { SchemaVersion = 2, SteamExe = steam, VSCodeExe = steam, CodexExe = steam, CodexHome = home };
                Installer.Install(args[1], target, settings, new[] { "123", "4294967294" });
                byte[] hooks = File.ReadAllBytes(Path.Combine(home, "hooks.json"));
                var config = new StateStore(target).Settings(); Assert(config.Accounts.Count == 2 && config.Accounts[1].AccountId == "4294967294", "large ID changed");
                var manifest = Json.Read<Installation>(Path.Combine(target, "installation.json")); Assert(manifest.Assets.Count == 8, "artwork missing");
                Installer.Install(args[1], target, config, new[] { "123" });
                Assert(hooks.SequenceEqual(File.ReadAllBytes(Path.Combine(home, "hooks.json"))), "repair duplicated hooks");
                Assert(!new StateStore(target).Load().Desired, "install started presence");
                // Uninstall integration without launching the deferred executable cleanup helper.
                string retained = Path.Combine(target, "user-note.txt"); File.WriteAllText(retained, "keep");
                string userArtwork = manifest.Assets[0].Path; File.WriteAllText(userArtwork, "user replacement");
                Installer.Uninstall(target, false);
                Assert(!File.ReadAllText(Path.Combine(home, "hooks.json")).Contains("CodexSteamBridge.Hook.exe"), "uninstall left hook");
                Assert(File.ReadAllText(userArtwork) == "user replacement" && File.Exists(retained), "uninstall removed custom content");
                Assert(!new StateStore(target).Settings().Enabled, "uninstall left enabled integration");
                Assert(manifest.Assets.Skip(1).All(a => !File.Exists(a.Path)), "owned artwork not removed");
            });
            Case("dead session owner can resume existing conversation", () => {
                var s = new BridgeState();
                s.Sessions.Add(new SessionInfo { Id = "s", OwnerPid = int.MaxValue, OwnerTicks = 1, Transcript = "old" });
                bool fresh = Notifications.Apply(s, Event("UserPromptSubmit", "resumed"), ProcessStamp.Current(), p => true, p => 0);
                Assert(fresh && s.Sessions[0].OwnerPid == Process.GetCurrentProcess().Id, "resumed conversation stuck on dead owner");
            });
            Case("late duplicate after completion remains suppressed", () => {
                var s = new BridgeState(); var owner = ProcessStamp.Current();
                Notifications.Apply(s, Event("UserPromptSubmit", "t"), owner, p => true, p => 0);
                s.Tasks.Clear(); var continuation = Event("UserPromptSubmit", "continued"); continuation["root_turn_id"] = "t";
                Assert(!Notifications.Apply(s, continuation, owner, p => true, p => 0), "completed-root continuation restarted");
            });
            Case("out of order stop cannot complete another task", () => {
                var s = Work(); Notifications.Apply(s, Event("Interrupt", "unknown"), null, p => true, p => 0);
                Assert(s.ActiveCount == 1, "unrelated stop removed task");
            });
            Case("cancel overtaking submit prevents stale launch", () => {
                var s = new BridgeState(); var owner = ProcessStamp.Current();
                Notifications.Apply(s, Event("Interrupt", "late"), owner, p => true, p => 0);
                Assert(!Notifications.Apply(s, Event("UserPromptSubmit", "late"), owner, p => true, p => 0), "overtaken submit resurrected task");
            });
            Case("delayed submit discovers already completed transcript", () => {
                string path = Path.Combine(root, "completed.jsonl");
                File.WriteAllText(path, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"late\"}}\n", new UTF8Encoding(false));
                var s = new BridgeState(); var input = Event("UserPromptSubmit", "late"); input["transcript_path"] = path;
                Notifications.Apply(s, input, ProcessStamp.Current(), p => true, p => TranscriptAdapter.InitialOffset(p));
                TranscriptAdapter.Reconcile(s); Assert(s.ActiveCount == 0, "completed task stayed active");
            });
            Case("invalid VDF input cannot overwrite configuration", () => {
                bool threw = false; try { Vdf.Read(new byte[] { 1, 0, 1 }); } catch (EndOfStreamException) { threw = true; }
                Assert(threw, "truncated VDF accepted");
            });
            Case("PID reuse is rejected", () => {
                var self = ProcessStamp.Current(); Assert(ProcessStamp.Alive(self.Pid, self.Ticks) && !ProcessStamp.Alive(self.Pid, self.Ticks + 1), "PID identity not checked");
            });
            Case("installer failure restores prior shortcut and files", () => {
                string folder = Path.Combine(root, "rollback install"), fakeSource = Path.Combine(folder, "source"), target = Path.Combine(folder, "target"), home = Path.Combine(folder, "home");
                Directory.CreateDirectory(fakeSource); Directory.CreateDirectory(target); Directory.CreateDirectory(home);
                foreach (string name in new[] { "CodexSteamBridge.exe", "CodexSteamBridge.Hook.exe", "README.md", "README.en.md", "LICENSE", "COMPATIBILITY.md" })
                    File.WriteAllText(Path.Combine(fakeSource, name), "fixture");
                File.WriteAllText(Path.Combine(target, "README.md"), "original");
                var settings = new Settings { SchemaVersion = 2, SteamExe = Path.Combine(fakeSource, "CodexSteamBridge.exe"), VSCodeExe = Path.Combine(fakeSource, "CodexSteamBridge.exe"), CodexExe = Path.Combine(fakeSource, "CodexSteamBridge.exe"), CodexHome = home };
                bool failed = false;
                try { Installer.Install(fakeSource, target, settings, new[] { "123" }); } catch (DirectoryNotFoundException) { failed = true; }
                Assert(failed && File.ReadAllText(Path.Combine(target, "README.md")) == "original" && !File.Exists(Path.Combine(target, "CodexSteamBridge.exe")), "partial installation not rolled back");
                Assert(!File.Exists(Installer.ShortcutPath(settings, "123")) && !File.Exists(Path.Combine(home, "hooks.json")), "configuration leaked after failure");
            });
            Case("completed state prunes conversation paths and tasks", () => {
                var store = new StateStore(Path.Combine(root, "pruned")); var s = Work();
                s.Sessions.Add(new SessionInfo { Id = "s", Transcript = "private-path" }); s.Tasks[0].Active = false; store.Save(s);
                var after = store.Load(); Assert(after.Tasks.Count == 0 && after.Sessions.Count == 0, "completed transcript retained");
            });
            Console.WriteLine("RESULT " + count + " passed"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    }
}
