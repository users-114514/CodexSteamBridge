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
            if (id.Length == 0 || id.Length > 322 || turn.Length > 256) return false;
            if (name == "Interrupt" && owner != null && turn.Length != 0 && !state.Seen.Contains(id + ":" + turn))
                state.Seen.Add(id + ":" + turn); // A cancel may overtake its submit handler.
            var session = state.Sessions.FirstOrDefault(s => s.Id == id);
            if (session != null && owner != null && (name == "SessionStart" || name == "UserPromptSubmit") &&
                (session.OwnerPid != owner.Pid || session.OwnerTicks != owner.Ticks || session.Transcript != Json.Str(input, "transcript_path")))
            {
                string replacement = Json.Str(input, "transcript_path");
                if (!validPath(replacement)) return false;
                if (session.Transcript != replacement && state.Tasks.Any(t => t.SessionId == id && t.Active))
                    session.Previous.Add(new SessionReader { Transcript = session.Transcript, Offset = session.Offset, OwnerPid = session.OwnerPid, OwnerTicks = session.OwnerTicks });
                session.OwnerPid = owner.Pid; session.OwnerTicks = owner.Ticks;
                if (session.Transcript != replacement) { session.Transcript = replacement; session.Offset = length(replacement); }
                session.Client = owner.Client;
            }
            if (session == null)
            {
                string transcript = Json.Str(input, "transcript_path");
                if (owner == null || !validPath(transcript) || (name != "SessionStart" && name != "UserPromptSubmit")) return false;
                session = new SessionInfo { Id = id, Transcript = transcript, OwnerPid = owner.Pid, OwnerTicks = owner.Ticks, Offset = length(transcript), Client = owner.Client };
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
                state.Tasks.Add(new TaskInfo { SessionId = id, TurnId = turn, RootId = root.Length == 0 ? turn : root,
                    OwnerPid = owner.Pid, OwnerTicks = owner.Ticks, Client = owner.Client });
                // A concurrently running reader may have passed this turn's start before its hook arrived.
                session.Offset = Math.Min(session.Offset, length(session.Transcript));
                return true;
            }
            var task = state.Tasks.FirstOrDefault(t => t.SessionId == id && (t.TurnId == turn || t.Aliases.Contains(turn)));
            if (name == "Interrupt")
            {
                if (task != null && owner != null && (task.OwnerPid == 0 || (task.OwnerPid == owner.Pid && task.OwnerTicks == owner.Ticks)))
                { task.Active = false; foreach (var agent in state.Agents.Where(a => a.SessionId == id && a.RootId == task.RootId)) agent.Active = false; }
            }
            else if (name == "SessionEnd" && owner != null && owner.Client != "cli")
            {
                foreach (var item in state.Tasks.Where(t => t.SessionId == id && (owner == null || t.OwnerPid == 0 || (t.OwnerPid == owner.Pid && t.OwnerTicks == owner.Ticks))))
                { item.Active = false; foreach (var agent in state.Agents.Where(a => a.SessionId == id && a.RootId == item.RootId)) agent.Active = false; }
            }
            else if (name == "SubagentStart" && task != null && task.Active)
            {
                string agentId = Json.Str(input, "agent_id");
                if (agentId.Length != 0 && agentId.Length <= 256 && !state.Agents.Any(a => a.Id == agentId && a.SessionId == id))
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
        public static string Source(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
            {
                // Only the first metadata record is examined; its prompt/body is never persisted.
                var line = new StringBuilder(); int next;
                while (line.Length < 4 * 1024 * 1024 && (next = reader.Read()) >= 0 && next != '\n') line.Append((char)next);
                var data = Json.Parse(line.ToString());
                if (Json.Str(data, "type") != "session_meta") throw new InvalidDataException("unsupported_source");
                var payload = Json.Obj(data, "payload");
                return Json.Str(payload, "source");
            }
        }
        public static bool UserTask(TaskInfo task)
        {
            if (task.RootId != task.TurnId) return false;
            return task.Client == "cli" ? task.Trigger == "user" :
                task.Trigger == "composer" || task.Trigger == "edit_user_message" || task.Trigger == "plan_implementation";
        }
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
                try
                {
                    foreach (var task in state.Tasks.Where(t => t.SessionId == session.Id && (t.Active || state.Agents.Any(a => a.Active && a.SessionId == t.SessionId && a.RootId == t.RootId))))
                        if (!ProcessStamp.Alive(task.OwnerPid == 0 ? session.OwnerPid : task.OwnerPid, task.OwnerPid == 0 ? session.OwnerTicks : task.OwnerTicks))
                        { task.Active = false; foreach (var agent in state.Agents.Where(a => a.SessionId == session.Id && a.RootId == task.RootId)) agent.Active = false; }
                    if (!state.Tasks.Any(t => t.SessionId == session.Id && t.Active) && !state.Agents.Any(a => a.SessionId == session.Id && a.Active)) continue;
                    foreach (var previous in session.Previous)
                        previous.Offset = Read(previous.Transcript, previous.Offset, payload => Apply(state, session.Id, payload));
                    session.Offset = Read(session.Transcript, session.Offset, payload => Apply(state, session.Id, payload));
                    foreach (var task in state.Tasks.Where(t => t.SessionId == session.Id && t.Active && t.AwaitStartUntil != 0))
                    {
                        if (task.Trigger.Length != 0)
                        { task.AwaitStartUntil = 0; if (!UserTask(task)) task.Active = false; }
                        else if (DateTime.UtcNow.Ticks >= task.AwaitStartUntil)
                        { task.Active = false; Profiles.Issue(state, session.ProfileId, "missing_task_start"); }
                    }
                    session.Previous.RemoveAll(r => !state.Tasks.Any(t => t.SessionId == session.Id && t.Active && t.OwnerPid == r.OwnerPid && t.OwnerTicks == r.OwnerTicks));
                }
                catch (InvalidDataException) { Reject(state, session, "unsupported_transcript"); }
                catch (IOException) { Reject(state, session, "transcript_unavailable"); }
                catch (ArgumentException) { Reject(state, session, "unsupported_transcript"); }
                catch (UnauthorizedAccessException) { Reject(state, session, "transcript_unavailable"); }
            }
            foreach (var agent in state.Agents.Where(a => a.Active && a.Transcript.Length != 0).ToArray())
            {
                try { agent.Offset = Read(agent.Transcript, agent.Offset, payload =>
                {
                    string type = Json.Str(payload, "type");
                    if (type == "task_complete" || type == "turn_aborted") agent.Active = false;
                    else if (type.StartsWith("task_", StringComparison.Ordinal) && type != "task_started") throw new InvalidDataException("unsupported_transcript");
                }); }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is ArgumentException) && !(error is UnauthorizedAccessException)) throw;
                    agent.Active = false;
                    var session = state.Sessions.FirstOrDefault(s => s.Id == agent.SessionId);
                    if (session != null) Profiles.Issue(state, session.ProfileId, "unsupported_transcript");
                }
            }
            state.Sessions.RemoveAll(s => !state.Tasks.Any(t => t.SessionId == s.Id && t.Active) && !state.Agents.Any(a => a.SessionId == s.Id && a.Active));
        }
        public static void Reject(BridgeState state, SessionInfo session, string reason)
        {
            foreach (var task in state.Tasks.Where(t => t.SessionId == session.Id)) task.Active = false;
            foreach (var agent in state.Agents.Where(a => a.SessionId == session.Id)) agent.Active = false;
            Profiles.Issue(state, session.ProfileId, reason);
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

        public void Receive(Dictionary<string, object> input) { Receive(input, ""); }
        public void Receive(Dictionary<string, object> input, string profileId)
        {
            var settings = store.Settings();
            if (!settings.Enabled) return;
            var profile = settings.CodexProfiles.FirstOrDefault(p => p.Id == (profileId.Length == 0 ? settings.DefaultProfileId : profileId));
            if (profile == null || !profile.Enabled) return;
            var owner = Native.CodexOwner(profile);
            if (owner == null) return;
            input = new Dictionary<string, object>(input);
            string rawSession = Json.Str(input, "session_id");
            if (rawSession.Length == 0 || rawSession.Length > 256) return;
            input["session_id"] = Profiles.SessionKey(profile.Id, rawSession);
            string transcript = Json.Str(input, "transcript_path");
            var mutex = store.Lock();
            try
            {
                var state = store.Load();
                if (Json.Str(input, "hook_event_name") == "UserPromptSubmit" || Json.Str(input, "hook_event_name") == "SessionStart")
                {
                    try
                    {
                        if (!TranscriptAdapter.ValidPath(transcript, profile.CodexHome)) throw new InvalidDataException("invalid_transcript_path");
                        string source = TranscriptAdapter.Source(transcript);
                        if (source != "cli" && source != "vscode") throw new InvalidDataException("unsupported_source");
                        Profiles.Issue(state, profile.Id, "");
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException) && !(error is ArgumentException) && !(error is UnauthorizedAccessException)) throw;
                        var rejected = state.Sessions.FirstOrDefault(s => s.Id == Json.Str(input, "session_id"));
                        if (rejected != null) TranscriptAdapter.Reject(state, rejected, "unsupported_source");
                        else Profiles.Issue(state, profile.Id, "unsupported_source");
                        store.Save(state); return;
                    }
                }
                bool fresh = Notifications.Apply(state, input, owner, p => TranscriptAdapter.ValidPath(p, profile.CodexHome), p => TranscriptAdapter.InitialOffset(p));
                var sessionInfo = state.Sessions.FirstOrDefault(s => s.Id == Json.Str(input, "session_id"));
                if (sessionInfo != null) sessionInfo.ProfileId = profile.Id;
                if (fresh)
                {
                    var incoming = state.Tasks.FirstOrDefault(t => t.SessionId == Json.Str(input, "session_id") && t.TurnId == Json.Str(input, "turn_id"));
                    if (incoming != null) incoming.AwaitStartUntil = DateTime.UtcNow.AddSeconds(2).Ticks;
                    try { TranscriptAdapter.Reconcile(state); }
                    catch (InvalidDataException) { Lifecycle.Stop(state, "unsupported_transcript"); store.Save(state); return; }
                    var submitted = state.Tasks.FirstOrDefault(t => t.SessionId == Json.Str(input, "session_id") && t.TurnId == Json.Str(input, "turn_id"));
                    if (submitted == null || !submitted.Active || (submitted.Trigger.Length != 0 && !TranscriptAdapter.UserTask(submitted)))
                    { store.Save(state); return; } // A model/hook continuation is not a new user request.
                    if (WorkerAlive(state) && state.Phase != "stopped") { /* Existing worker aggregates the new root. */ }
                    else
                    {
                        var steam = SteamPlatform.Inspect(settings);
                        Lifecycle.Begin(state, steam, DateTime.UtcNow.Ticks, false);
                        state.ActivationSessionId = submitted.AwaitStartUntil != 0 ? submitted.SessionId : "";
                        state.ActivationTurnId = submitted.AwaitStartUntil != 0 ? submitted.TurnId : "";
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
                            if (current.ActivationTurnId.Length != 0)
                            {
                                var activation = current.Tasks.FirstOrDefault(t => t.SessionId == current.ActivationSessionId && t.TurnId == current.ActivationTurnId && t.Active);
                                if (activation == null || (activation.AwaitStartUntil == 0 && !TranscriptAdapter.UserTask(activation)))
                                    Lifecycle.Stop(current, "not_user_submission");
                                if (activation == null || activation.AwaitStartUntil == 0) { current.ActivationTurnId = ""; current.ActivationSessionId = ""; }
                            }
                            var steam = SteamPlatform.Inspect(settings);
                            bool pending = current.ActivationTurnId.Length != 0 || (current.Tasks.Any(t => t.Active) && current.Tasks.Where(t => t.Active).All(t => t.AwaitStartUntil != 0));
                            if (pending && !current.LaunchRequested && current.Phase != "stopped" && settings.Enabled && Lifecycle.Gate(steam).Length == 0 &&
                                current.SteamPid == steam.Pid && current.SteamTicks == steam.StartTicks)
                            { current.HeartbeatTicks = DateTime.UtcNow.Ticks; action = NextAction.Wait; }
                            else action = Lifecycle.Tick(current, settings, steam, DateTime.UtcNow.Ticks, PresenceAlive(current));
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
                        if (!automatic) { Lifecycle.Begin(state, steam, DateTime.UtcNow.Ticks, true); state.ActivationSessionId = ""; state.ActivationTurnId = ""; }
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
