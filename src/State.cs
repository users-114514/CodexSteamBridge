using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexSteamBridge
{
    public sealed class AccountConfig
    {
        public string AccountId = "";
        public string AppId = "";
        public string GameId = "";
    }

    public sealed class Settings
    {
        public int SchemaVersion = 2;
        public bool Enabled = true;
        public string SteamExe = "";
        public string VSCodeExe = "";
        public string CodexExe = "";
        public string CodexHome = "";
        public string Language = "zh-CN";
        public List<AccountConfig> Accounts = new List<AccountConfig>();
    }

    public sealed class SessionInfo
    {
        public string Id = "";
        public string Transcript = "";
        public int OwnerPid;
        public long OwnerTicks;
        public long Offset;
    }

    public sealed class TaskInfo
    {
        public string SessionId = "";
        public string TurnId = "";
        public string RootId = "";
        public string Trigger = "";
        public bool Active = true;
        public List<string> Aliases = new List<string>();
    }

    public sealed class AgentInfo
    {
        public string Id = "";
        public string SessionId = "";
        public string RootId = "";
        public string Transcript = "";
        public long Offset;
        public bool Active = true;
    }

    // Only operational metadata is persisted. Never add prompts or message text here.
    public sealed class BridgeState
    {
        public int SchemaVersion = 2;
        public string Phase = "idle";
        public string Reason = "idle";
        public long Epoch;
        public long SuppressedEpoch;
        public bool Desired;
        public int WorkerPid;
        public long WorkerTicks;
        public long HeartbeatTicks;
        public int PresencePid;
        public long PresenceTicks;
        public int SteamPid;
        public long SteamTicks;
        public string AccountId = "";
        public long DeadlineTicks;
        public bool LaunchRequested;
        public List<SessionInfo> Sessions = new List<SessionInfo>();
        public List<TaskInfo> Tasks = new List<TaskInfo>();
        public List<AgentInfo> Agents = new List<AgentInfo>();
        public List<string> Seen = new List<string>();
        public int ActiveCount { get { return Tasks.Count(t => t.Active) + Agents.Count(a => a.Active); } }
    }

    public sealed class SteamObservation
    {
        public bool Installed;
        public bool Running;
        public int Pid;
        public long StartTicks;
        public string AccountId = "";
        public bool Configured;
    }

    public enum NextAction { Wait, Launch, Exit }

    // Pure transition function: no file/process/network operations and no automatic retries.
    public static class Lifecycle
    {
        public static string Gate(SteamObservation steam)
        {
            if (!steam.Installed) return "steam_missing";
            if (!steam.Running) return "steam_not_running";
            if (steam.AccountId.Length != 0 && !steam.Configured) return "account_not_configured";
            return "";
        }

        public static void Begin(BridgeState state, SteamObservation steam, long now, bool manual)
        {
            state.Epoch++;
            state.SteamPid = steam.Pid;
            state.SteamTicks = steam.StartTicks;
            state.AccountId = steam.AccountId;
            state.Desired = false;
            state.LaunchRequested = false;
            state.Reason = Gate(steam);
            state.Phase = "starting";
            state.DeadlineTicks = now + TimeSpan.FromSeconds(60).Ticks;
            if (state.Reason.Length != 0) Stop(state, state.Reason);
            else if (state.ActiveCount == 0) Stop(state, "idle");
            else if (steam.AccountId.Length == 0) { state.Phase = "waiting_login"; state.Reason = "waiting_login"; }
            else if (manual) { state.Phase = "active"; state.Desired = true; }
        }

        public static void Stop(BridgeState state, string reason)
        {
            state.Desired = false;
            state.Phase = "stopped";
            state.Reason = reason;
            state.SuppressedEpoch = state.Epoch;
        }

        public static NextAction Tick(BridgeState state, Settings settings, SteamObservation steam,
                                      long now, bool presenceAlive)
        {
            state.HeartbeatTicks = now;
            if (state.Phase == "stopped") return NextAction.Exit;
            if (!settings.Enabled) { Stop(state, "disabled"); return NextAction.Exit; }
            if (state.ActiveCount == 0) { Stop(state, "idle"); return NextAction.Exit; }
            string gate = Gate(steam);
            if (gate.Length != 0) { Stop(state, gate); return NextAction.Exit; }
            if (steam.Pid != state.SteamPid || steam.StartTicks != state.SteamTicks)
            { Stop(state, "steam_restarted"); return NextAction.Exit; }
            if (state.Phase == "waiting_login")
            {
                if (now >= state.DeadlineTicks) { Stop(state, "login_timeout"); return NextAction.Exit; }
                if (steam.AccountId.Length == 0) return NextAction.Wait;
                state.AccountId = steam.AccountId;
                state.Phase = "starting";
            }
            else if (steam.AccountId != state.AccountId)
            { Stop(state, "account_changed"); return NextAction.Exit; }
            if (presenceAlive)
            {
                state.Phase = "active";
                state.Desired = true;
                state.Reason = "active";
                return NextAction.Wait;
            }
            if (state.Phase == "active") { Stop(state, "presence_stopped"); return NextAction.Exit; }
            if (state.LaunchRequested)
            {
                if (now >= state.DeadlineTicks) { Stop(state, "launch_timeout"); return NextAction.Exit; }
                return NextAction.Wait;
            }
            state.Desired = true;
            state.LaunchRequested = true;
            state.Phase = "launching";
            state.Reason = "waiting_presence";
            state.DeadlineTicks = now + TimeSpan.FromSeconds(10).Ticks;
            return NextAction.Launch;
        }
    }

    public static class Json
    {
        public static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024, RecursionLimit = 64 }; }
        public static T Read<T>(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return Serializer().Deserialize<T>(reader.ReadToEnd());
        }
        public static Dictionary<string, object> Parse(string text)
        { return Serializer().Deserialize<Dictionary<string, object>>(text); }
        public static string Str(Dictionary<string, object> data, string key)
        { object value; return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : ""; }
        public static Dictionary<string, object> Obj(Dictionary<string, object> data, string key)
        { object value; return data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        public static void Write(string path, object data) { WriteBytes(path, new UTF8Encoding(false).GetBytes(Serializer().Serialize(data))); }
        public static void WriteBytes(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data, 0, data.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public sealed class StateStore
    {
        public readonly string Root;
        public string StatePath { get { return Path.Combine(Root, "runtime", "state.json"); } }
        public string SettingsPath { get { return Path.Combine(Root, "settings.json"); } }
        public StateStore(string root) { Root = Path.GetFullPath(root); }
        public string Key
        {
            get
            {
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant() +
                        System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))).Replace("-", "").Substring(0, 24);
            }
        }
        public Mutex Lock()
        {
            var mutex = new Mutex(false, "Local\\CodexSteamBridge.State." + Key);
            try { if (!mutex.WaitOne(2000)) throw new IOException("State busy"); }
            catch (AbandonedMutexException) { }
            catch { mutex.Dispose(); throw; }
            return mutex;
        }
        public BridgeState Load()
        {
            if (!File.Exists(StatePath)) return new BridgeState();
            var state = Json.Read<BridgeState>(StatePath);
            if (state == null || state.SchemaVersion != 2) throw new InvalidDataException("Unsupported state version");
            return state;
        }
        public Settings Settings()
        {
            var settings = Json.Read<Settings>(SettingsPath);
            if (settings == null || settings.SchemaVersion != 2) throw new InvalidDataException("Unsupported configuration version");
            return settings;
        }
        public void Save(BridgeState state)
        {
            // Bound deduplication metadata; no event journal is retained.
            if (state.Seen.Count > 256) state.Seen.RemoveRange(0, state.Seen.Count - 256);
            state.Tasks.RemoveAll(t => !t.Active && !state.Agents.Any(a => a.Active && a.SessionId == t.SessionId && a.RootId == t.RootId));
            state.Agents.RemoveAll(a => !a.Active);
            state.Sessions.RemoveAll(s => !state.Tasks.Any(t => t.Active && t.SessionId == s.Id) && !state.Agents.Any(a => a.Active && a.SessionId == s.Id));
            Json.Write(StatePath, state);
        }
        public static void Unlock(Mutex mutex) { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
