using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CodexSteamBridge
{
    public static class Notifications
    {
        public static bool Apply(BridgeState state, Dictionary<string, object> input, ProcessStamp owner,
                                 Func<string, bool> validPath, Func<string, long> length)
        {
            string name = Json.Str(input, "hook_event_name"), id = Json.Str(input, "session_id"), turn = Json.Str(input, "turn_id");
            if (id.Length == 0 || id.Length > 256 || turn.Length > 256) return false;
            if (name == "Interrupt" && owner != null && turn.Length != 0 && !state.Seen.Contains(id + ":" + turn))
                state.Seen.Add(id + ":" + turn); // A cancel may overtake its submit handler.
            var session = state.Sessions.FirstOrDefault(s => s.Id == id);
            if (session != null && owner != null && (name == "SessionStart" || name == "UserPromptSubmit") &&
                (session.OwnerPid != owner.Pid || session.OwnerTicks != owner.Ticks) && !ProcessStamp.Alive(session.OwnerPid, session.OwnerTicks))
            {
                string replacement = Json.Str(input, "transcript_path");
                if (!validPath(replacement)) return false;
                foreach (var item in state.Tasks.Where(t => t.SessionId == id)) item.Active = false;
                foreach (var item in state.Agents.Where(a => a.SessionId == id)) item.Active = false;
                session.OwnerPid = owner.Pid; session.OwnerTicks = owner.Ticks;
                session.Transcript = replacement; session.Offset = length(replacement);
            }
            if (session == null)
            {
                string transcript = Json.Str(input, "transcript_path");
                if (owner == null || !validPath(transcript) || (name != "SessionStart" && name != "UserPromptSubmit")) return false;
                session = new SessionInfo { Id = id, Transcript = transcript, OwnerPid = owner.Pid, OwnerTicks = owner.Ticks, Offset = length(transcript) };
                state.Sessions.Add(session);
            }
            if (name == "UserPromptSubmit")
            {
                if (owner == null || owner.Pid != session.OwnerPid || owner.Ticks != session.OwnerTicks || turn.Length == 0) return false;
                string key = id + ":" + turn;
                if (state.Seen.Contains(key)) return false;
                // Continuations associated with an existing root cannot re-enable a stopped epoch.
                string root = Json.Str(input, "root_turn_id");
                if (root.Length != 0 && root != turn && state.Seen.Contains(id + ":" + root))
                { state.Seen.Add(key); return false; }
                var existing = state.Tasks.FirstOrDefault(t => t.SessionId == id && (t.TurnId == turn || t.Aliases.Contains(turn) || (root.Length != 0 && t.RootId == root)));
                state.Seen.Add(key);
                if (existing != null) return false;
                state.Tasks.Add(new TaskInfo { SessionId = id, TurnId = turn, RootId = root.Length == 0 ? turn : root });
                return true;
            }
            var task = state.Tasks.FirstOrDefault(t => t.SessionId == id && (t.TurnId == turn || t.Aliases.Contains(turn)));
            if (name == "Interrupt")
            {
                if (task != null)
                { task.Active = false; foreach (var agent in state.Agents.Where(a => a.SessionId == id && a.RootId == task.RootId)) agent.Active = false; }
            }
            else if (name == "SessionEnd")
            { foreach (var item in state.Tasks.Where(t => t.SessionId == id)) item.Active = false; foreach (var agent in state.Agents.Where(a => a.SessionId == id)) agent.Active = false; }
            else if (name == "SubagentStart" && task != null)
            {
                string agentId = Json.Str(input, "agent_id");
                if (agentId.Length != 0 && agentId.Length <= 256 && !state.Agents.Any(a => a.Id == agentId))
                    state.Agents.Add(new AgentInfo { Id = agentId, SessionId = id, RootId = task.RootId });
            }
            else if (name == "SubagentStop")
            {
                var agent = state.Agents.FirstOrDefault(a => a.Id == Json.Str(input, "agent_id") && a.SessionId == id);
                if (agent != null)
                {
                    string path = Json.Str(input, "agent_transcript_path");
                    if (validPath(path)) agent.Transcript = path;
                    else agent.Active = false; // End notification is the only available boundary on older versions.
                }
            }
            // Stop is advisory: another hook may continue the turn. Transcript completion is authoritative.
            return false;
        }
    }

    public static class TranscriptAdapter
    {
        public static long InitialOffset(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length <= 1024 * 1024) return 0;
                stream.Position = stream.Length - 1024 * 1024;
                while (stream.Position < stream.Length && stream.ReadByte() != 10) { }
                return stream.Position;
            }
        }
        public static bool ValidPath(string path, string home)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
            string full = Path.GetFullPath(path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path);
            string sessions = Path.GetFullPath(Path.Combine(home, "sessions")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(sessions, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return false;
            string cursor = full;
            while (cursor.Length >= sessions.Length)
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false;
                cursor = Path.GetDirectoryName(cursor);
            }
            return true;
        }

        public static long Read(string path, long offset, Action<Dictionary<string, object>> consume)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (offset > stream.Length) throw new InvalidDataException("transcript_truncated");
                stream.Position = offset;
                long committed = offset, budget = 4 * 1024 * 1024;
                using (var line = new MemoryStream())
                {
                    bool oversized = false;
                    int value;
                    while (budget-- > 0 && (value = stream.ReadByte()) != -1)
                    {
                        if (value == 10)
                        {
                            if (!oversized && line.Length != 0)
                            {
                                var data = Json.Parse(new UTF8Encoding(false, true).GetString(line.ToArray()));
                                if (Json.Str(data, "type") == "event_msg")
                                {
                                    var payload = Json.Obj(data, "payload");
                                    if (payload != null) consume(payload);
                                }
                            }
                            committed = stream.Position; line.SetLength(0); oversized = false;
                        }
                        else if (!oversized)
                        {
                            if (line.Length < 2 * 1024 * 1024) line.WriteByte((byte)value);
                            else { oversized = true; line.SetLength(0); }
                        }
                    }
                }
                return committed;
            }
        }

        public static void Reconcile(BridgeState state)
        {
            foreach (var session in state.Sessions.ToArray())
            {
                if (!ProcessStamp.Alive(session.OwnerPid, session.OwnerTicks))
                {
                    foreach (var task in state.Tasks.Where(t => t.SessionId == session.Id)) task.Active = false;
                    foreach (var agent in state.Agents.Where(a => a.SessionId == session.Id)) agent.Active = false;
                    state.Sessions.Remove(session); continue;
                }
                if (!state.Tasks.Any(t => t.SessionId == session.Id && t.Active) && !state.Agents.Any(a => a.SessionId == session.Id && a.Active)) continue;
                session.Offset = Read(session.Transcript, session.Offset, payload => Apply(state, session.Id, payload));
            }
            foreach (var agent in state.Agents.Where(a => a.Active && a.Transcript.Length != 0).ToArray())
                agent.Offset = Read(agent.Transcript, agent.Offset, payload =>
                {
                    string type = Json.Str(payload, "type");
                    if (type == "task_complete" || type == "turn_aborted") agent.Active = false;
                    else if (type.StartsWith("task_", StringComparison.Ordinal) && type != "task_started") throw new InvalidDataException("unsupported_transcript");
                });
            state.Sessions.RemoveAll(s => !ProcessStamp.Alive(s.OwnerPid, s.OwnerTicks));
        }
        public static void Apply(BridgeState state, string sessionId, Dictionary<string, object> payload)
        {
            string type = Json.Str(payload, "type"), turn = Json.Str(payload, "turn_id"), root = Json.Str(payload, "root_turn_id");
            if (type.StartsWith("task_", StringComparison.Ordinal) && type != "task_started" && type != "task_complete") throw new InvalidDataException("unsupported_transcript");
            if ((type == "task_started" || type == "task_complete") && turn.Length == 0 && root.Length == 0) throw new InvalidDataException("unsupported_transcript");
            var task = state.Tasks.FirstOrDefault(t => t.SessionId == sessionId &&
                (t.TurnId == turn || t.Aliases.Contains(turn) || (root.Length != 0 && t.RootId == root)));
            if (task == null) return;
            if (type == "task_started")
            {
                var attribution = Json.Obj(payload, "turn_attribution");
                if (root.Length == 0 && attribution != null) root = Json.Str(attribution, "root_turn_id");
                if (root.Length != 0) task.RootId = root;
                if (attribution != null) task.Trigger = Json.Str(attribution, "turn_trigger");
                if (turn.Length != 0 && !task.Aliases.Contains(turn)) task.Aliases.Add(turn);
            }
            else if (type == "task_complete" || type == "turn_aborted")
            {
                task.Active = false;
                if (type == "turn_aborted") foreach (var agent in state.Agents.Where(a => a.SessionId == sessionId && a.RootId == task.RootId)) agent.Active = false;
            }
        }
    }

    public sealed class BridgeRuntime
    {
        readonly StateStore store;
        public BridgeRuntime(string root) { store = new StateStore(root); }
        public static string ExecutableRoot { get { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } }
        static bool WorkerAlive(BridgeState state) { return ProcessStamp.Alive(state.WorkerPid, state.WorkerTicks); }
        static bool PresenceAlive(BridgeState state) { return ProcessStamp.Alive(state.PresencePid, state.PresenceTicks); }

        void StartWorker(BridgeState state)
        {
            using (var process = Process.Start(new ProcessStartInfo(Path.Combine(store.Root, "CodexSteamBridge.exe"), "run")
            { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = store.Root }))
            {
                // ShellExecute starts the GUI worker without inheriting the
                // hook's pipe handles. Redirecting new standard handles still
                // inherits other open pipes on .NET Framework CreateProcess.
                state.WorkerPid = process.Id; state.WorkerTicks = process.StartTime.ToUniversalTime().Ticks; state.HeartbeatTicks = DateTime.UtcNow.Ticks;
            }
        }

        public void Receive(Dictionary<string, object> input)
        {
            var settings = store.Settings();
            if (!settings.Enabled) return;
            var owner = Native.CodexOwner();
            var mutex = store.Lock();
            try
            {
                var state = store.Load();
                bool fresh = Notifications.Apply(state, input, owner, p => TranscriptAdapter.ValidPath(p, settings.CodexHome), p => TranscriptAdapter.InitialOffset(p));
                if (fresh)
                {
                    try { TranscriptAdapter.Reconcile(state); }
                    catch (InvalidDataException) { Lifecycle.Stop(state, "unsupported_transcript"); store.Save(state); return; }
                    var submitted = state.Tasks.FirstOrDefault(t => t.SessionId == Json.Str(input, "session_id") && t.TurnId == Json.Str(input, "turn_id"));
                    if (submitted != null && (submitted.RootId != submitted.TurnId ||
                        (submitted.Trigger.Length != 0 && submitted.Trigger != "composer" && submitted.Trigger != "plan_implementation")))
                    { store.Save(state); return; } // A model/hook continuation is not a new user request.
                    if (WorkerAlive(state) && state.Phase != "stopped") { /* Existing worker aggregates the new root. */ }
                    else
                    {
                        var steam = SteamPlatform.Inspect(settings);
                        Lifecycle.Begin(state, steam, DateTime.UtcNow.Ticks, false);
                        if (state.Phase != "stopped") StartWorker(state);
                    }
                }
                else if (Json.Str(input, "hook_event_name") == "Stop" || Json.Str(input, "hook_event_name") == "Interrupt" || Json.Str(input, "hook_event_name") == "SessionEnd")
                {
                    // Even when the worker is suppressed, retire completed task metadata.
                    try { TranscriptAdapter.Reconcile(state); }
                    catch (InvalidDataException) { Lifecycle.Stop(state, "unsupported_transcript"); }
                    catch (IOException) { Lifecycle.Stop(state, "transcript_unavailable"); }
                }
                if (!WorkerAlive(state) && state.Phase != "stopped" && state.Desired)
                    Lifecycle.Stop(state, "runtime_stopped");
                store.Save(state);
            }
            finally { StateStore.Unlock(mutex); }
        }

        public void Run()
        {
                var self = ProcessStamp.Current();
                try
                {
                    while (true)
                    {
                        NextAction action; BridgeState current; Settings settings;
                        var mutex = store.Lock();
                        try
                        {
                            current = store.Load(); settings = store.Settings();
                            // The spawn and PID handoff happen under the state lock. A terminating
                            // worker cannot overwrite a successor, even before its process exits.
                            if (current.WorkerPid != self.Pid || current.WorkerTicks != self.Ticks) return;
                            try { TranscriptAdapter.Reconcile(current); }
                            catch (InvalidDataException) { Lifecycle.Stop(current, "unsupported_transcript"); }
                            catch (FileNotFoundException) { Lifecycle.Stop(current, "transcript_unavailable"); }
                            catch (DirectoryNotFoundException) { Lifecycle.Stop(current, "transcript_unavailable"); }
                            action = Lifecycle.Tick(current, settings, SteamPlatform.Inspect(settings), DateTime.UtcNow.Ticks, PresenceAlive(current));
                            if (action == NextAction.Exit) current.WorkerPid = 0;
                            store.Save(current);
                        }
                        finally { StateStore.Unlock(mutex); }
                        if (action == NextAction.Exit) return;
                        if (action == NextAction.Launch)
                        {
                            mutex = store.Lock();
                            try
                            {
                                current = store.Load();
                                if (current.WorkerPid != self.Pid || current.WorkerTicks != self.Ticks) return;
                                if (!SteamPlatform.Launch(settings, current)) { Lifecycle.Stop(current, "launch_failed"); store.Save(current); }
                            }
                            finally { StateStore.Unlock(mutex); }
                        }
                        Thread.Sleep(250);
                    }
                }
                catch
                {
                    // Failure ends this epoch. There is deliberately no restart scheduler.
                    try
                    {
                        var mutex = store.Lock();
                        try { var state = store.Load(); if (state.WorkerPid == self.Pid) { Lifecycle.Stop(state, "runtime_failed"); state.WorkerPid = 0; store.Save(state); } }
                        finally { StateStore.Unlock(mutex); }
                    }
                    catch { }
                }
        }

        public void Presence()
        {
            bool created;
            using (var singleton = new Mutex(true, "Local\\CodexSteamBridge.PresenceV2." + store.Key, out created))
            {
                if (!created) return;
                var self = ProcessStamp.Current();
                try
                {
                    var mutex = store.Lock();
                    try
                    {
                        var state = store.Load(); var settings = store.Settings();
                        if (!settings.Enabled) return;
                        TranscriptAdapter.Reconcile(state);
                        if (state.ActiveCount == 0) return;
                        var steam = SteamPlatform.Inspect(settings);
                        if (Lifecycle.Gate(steam).Length != 0 || steam.AccountId.Length == 0) return;
                        var account = settings.Accounts.FirstOrDefault(a => a.AccountId == steam.AccountId);
                        string game = Environment.GetEnvironmentVariable("SteamGameId");
                        if (account == null || game != account.GameId) return; // Must be launched by the configured Steam shortcut.
                        bool automatic = WorkerAlive(state) && state.Phase == "launching" && DateTime.UtcNow.Ticks < state.DeadlineTicks;
                        if (!automatic) Lifecycle.Begin(state, steam, DateTime.UtcNow.Ticks, true);
                        state.PresencePid = self.Pid; state.PresenceTicks = self.Ticks; state.Desired = true;
                        if (!WorkerAlive(state)) StartWorker(state);
                        store.Save(state);
                    }
                    finally { StateStore.Unlock(mutex); }
                    var lastRead = Stopwatch.StartNew();
                    while (true)
                    {
                        try
                        {
                            var state = store.Load();
                            lastRead.Restart();
                            if (!state.Desired || !WorkerAlive(state) || DateTime.UtcNow.Ticks - state.HeartbeatTicks > TimeSpan.FromSeconds(4).Ticks) return;
                        }
                        catch (IOException) { if (lastRead.ElapsedMilliseconds >= 4000) return; }
                        catch (ArgumentException) { if (lastRead.ElapsedMilliseconds >= 4000) return; }
                        Thread.Sleep(250);
                    }
                }
                finally { singleton.ReleaseMutex(); }
            }
        }
    }
}
