using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CodexSteamBridge
{
    public sealed class OwnedAsset { public string Path = ""; public string Hash = ""; }
    public sealed class Installation
    {
        public string Version = "0.1.0";
        public string Root = "";
        public string CodexHome = "";
        public List<string> HookKeys = new List<string>();
        public List<string> Files = new List<string>();
        public List<OwnedAsset> Assets = new List<OwnedAsset>();
    }
    public sealed class FileTransaction : IDisposable
    {
        readonly Dictionary<string, byte[]> before = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        bool committed;
        public void Write(string path, byte[] data)
        {
            if (data != null && File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(data)) return;
            if (!before.ContainsKey(path)) before[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (data == null) { if (File.Exists(path)) File.Delete(path); }
            else Json.WriteBytes(path, data);
        }
        public void WriteJson(string path, object value) { Write(path, Encoding.UTF8.GetBytes(Json.Serializer().Serialize(value))); }
        public void Commit() { committed = true; }
        public void Dispose()
        {
            if (committed) return;
            foreach (var item in before.Reverse())
            { if (item.Value == null) { if (File.Exists(item.Key)) File.Delete(item.Key); } else Json.WriteBytes(item.Key, item.Value); }
        }
    }

    public static class Hooks
    {
        public static readonly string[] Events = { "SessionStart", "UserPromptSubmit", "Stop", "Interrupt", "SessionEnd", "SubagentStart", "SubagentStop" };
        public static string Command(string root)
        { return "powershell.exe -NoProfile -NonInteractive -Command \"& '" + Path.Combine(root, "CodexSteamBridge.Hook.exe").Replace("'", "''") + "' event\""; }
        static List<object> List(object value)
        { return value is object[] ? ((object[])value).ToList() : value is ArrayList ? ((ArrayList)value).Cast<object>().ToList() : value as List<object>; }
        public static bool Owns(string command, string root)
        { return command == Command(root); }

        public static byte[] Merge(byte[] original, string root, bool remove)
        {
            var document = original == null ? new Dictionary<string, object>() : Json.Parse(Encoding.UTF8.GetString(original).TrimStart('\ufeff'));
            if (document == null) throw new InvalidDataException("Invalid hooks.json");
            var hooks = Json.Obj(document, "hooks");
            if (hooks == null)
            {
                if (document.ContainsKey("hooks")) throw new InvalidDataException("Invalid hooks structure");
                hooks = new Dictionary<string, object>(); document["hooks"] = hooks;
            }
            foreach (string name in Events)
            {
                object raw; var groups = hooks.TryGetValue(name, out raw) ? List(raw) : new List<object>();
                if (groups == null) throw new InvalidDataException("Invalid hook event " + name);
                bool found = false;
                var output = new List<object>();
                foreach (object value in groups)
                {
                    var group = value as Dictionary<string, object>;
                    if (group == null || !group.TryGetValue("hooks", out raw)) throw new InvalidDataException("Invalid hook group");
                    var handlers = List(raw);
                    if (handlers == null) throw new InvalidDataException("Invalid hook handlers");
                    var kept = new List<object>();
                    foreach (object handlerValue in handlers)
                    {
                        var handler = handlerValue as Dictionary<string, object>;
                        if (handler == null) throw new InvalidDataException("Invalid hook handler");
                        if (Owns(Json.Str(handler, "command"), root))
                        {
                            if (remove || found) continue;
                            found = true;
                        }
                        kept.Add(handler);
                    }
                    if (kept.Count > 0) { group["hooks"] = kept; output.Add(group); }
                }
                if (!remove && !found)
                    output.Add(new Dictionary<string, object> { { "hooks", new object[] { new Dictionary<string, object> {
                        { "type", "command" }, { "command", Command(root) }, { "timeout", 3 } } } } });
                if (output.Count == 0) hooks.Remove(name); else hooks[name] = output;
            }
            return Encoding.UTF8.GetBytes(Json.Serializer().Serialize(document));
        }
        public static byte[] RemoveTrust(byte[] original, IEnumerable<string> keys)
        {
            var owned = new HashSet<string>(keys, StringComparer.Ordinal);
            string text = Encoding.UTF8.GetString(original).TrimStart('\ufeff');
            var output = new StringBuilder(); bool skip = false;
            foreach (string line in text.Split('\n'))
            {
                string clean = line.Trim();
                if (clean.StartsWith("[", StringComparison.Ordinal))
                {
                    skip = false;
                    const string prefix = "[hooks.state.";
                    if (clean.StartsWith(prefix, StringComparison.Ordinal) && clean.EndsWith("]", StringComparison.Ordinal))
                    {
                        string key = clean.Substring(prefix.Length, clean.Length - prefix.Length - 1);
                        try
                        {
                            if (key.StartsWith("'", StringComparison.Ordinal) && key.EndsWith("'", StringComparison.Ordinal)) key = key.Substring(1, key.Length - 2);
                            else if (key.StartsWith("\"", StringComparison.Ordinal)) key = Json.Serializer().Deserialize<string>(key);
                            skip = owned.Contains(key);
                        }
                        catch { skip = false; }
                    }
                }
                if (!skip) output.Append(line).Append('\n');
            }
            return Encoding.UTF8.GetBytes(output.ToString().TrimEnd('\n'));
        }
    }

    public static class TrustProbe
    {
        static Dictionary<string, object> Response(Process process, int id, int timeout)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeout)
            {
                var line = process.StandardOutput.ReadLineAsync();
                if (!line.Wait(Math.Max(1, timeout - (int)clock.ElapsedMilliseconds)) || line.Result == null) throw new IOException("Codex interface timed out");
                var value = Json.Parse(line.Result);
                object found;
                if (value.TryGetValue("id", out found) && Convert.ToString(found) == id.ToString())
                { var result = Json.Obj(value, "result"); if (result == null) throw new IOException("Codex interface rejected request"); return result; }
            }
            throw new IOException("Codex interface timed out");
        }
        public static List<Dictionary<string, object>> Inspect(Settings settings, string root)
        {
            if (!File.Exists(settings.CodexExe)) throw new FileNotFoundException("Codex executable not found");
            var info = new ProcessStartInfo(settings.CodexExe, "app-server --stdio") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            info.EnvironmentVariables["CODEX_HOME"] = settings.CodexHome;
            using (var process = Process.Start(info))
            {
                process.ErrorDataReceived += (s, e) => { }; process.BeginErrorReadLine();
                try
                {
                    process.StandardInput.WriteLine(Json.Serializer().Serialize(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "codex_steam_bridge", version = "0.1.0" }, capabilities = new { } } }));
                    process.StandardInput.Flush(); Response(process, 1, 8000);
                    process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                    process.StandardInput.WriteLine(Json.Serializer().Serialize(new { id = 2, method = "hooks/list", @params = new { cwds = new[] { root } } }));
                    process.StandardInput.Flush();
                    var response = Response(process, 2, 8000); object data;
                    var result = new List<Dictionary<string, object>>();
                    if (!response.TryGetValue("data", out data)) return result;
                    foreach (Dictionary<string, object> layer in (IEnumerable)data)
                    {
                        object list; if (!layer.TryGetValue("hooks", out list)) continue;
                        foreach (Dictionary<string, object> hook in (IEnumerable)list)
                            if (Hooks.Owns(Json.Str(hook, "command"), root)) result.Add(hook);
                    }
                    return result;
                }
                finally { if (!process.HasExited) process.Kill(); process.WaitForExit(3000); }
            }
        }
    }

    public static class Installer
    {
        public static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string ShortcutPath(Settings settings, string account)
        { uint id; if (!uint.TryParse(account, out id) || id == 0) throw new InvalidDataException("Invalid account ID"); return Path.Combine(Path.GetDirectoryName(settings.SteamExe), "userdata", account, "config", "shortcuts.vdf"); }
        public static bool Same(byte[] a, byte[] b) { return a != null && b != null && a.SequenceEqual(b); }
        public static void ValidateRoot(string root)
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Length < 5 || full == Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar)) throw new IOException("Choose an application directory");
            string cursor = full;
            while (!string.IsNullOrEmpty(cursor))
            { if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Installation path cannot contain a directory link"); cursor = Path.GetDirectoryName(cursor); }
        }
        static void StopOwnProcesses(string root)
        {
            foreach (var process in Process.GetProcessesByName("CodexSteamBridge"))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id != Process.GetCurrentProcess().Id && string.Equals(process.MainModule.FileName, Path.Combine(root, "CodexSteamBridge.exe"), StringComparison.OrdinalIgnoreCase))
                        { process.Kill(); if (!process.WaitForExit(3000)) throw new IOException("Cannot stop previous bridge"); }
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        public static string RunTaskCommand(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                string output = process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
                if (!process.WaitForExit(5000)) { process.Kill(); throw new IOException("Task scheduler timed out"); }
                return process.ExitCode == 0 ? output : "";
            }
        }
        static bool OwnLegacyTask(string root)
        {
            string xml = RunTaskCommand("/Query /TN CodexSteamBridge /XML");
            if (xml.Length == 0) return false;
            var document = new System.Xml.XmlDocument(); document.LoadXml(xml);
            var commands = document.GetElementsByTagName("Command");
            return commands.Count == 1 && string.Equals(commands[0].InnerText, Path.Combine(root, "CodexSteamBridge.exe"), StringComparison.OrdinalIgnoreCase);
        }
        static BridgeState MigrateState(string root)
        {
            string path = Path.Combine(root, "runtime", "state.json");
            if (!File.Exists(path)) return new BridgeState { Reason = "waiting_new_task" };
            var data = Json.Parse(File.ReadAllText(path, Encoding.UTF8));
            BridgeState state;
            if (Json.Str(data, "SchemaVersion") == "2") state = Json.Read<BridgeState>(path);
            else
            {
                state = new BridgeState(); object sessions;
                if (data.TryGetValue("Sessions", out sessions))
                    foreach (Dictionary<string, object> item in (IEnumerable)sessions)
                    {
                        var session = new SessionInfo { Id = Json.Str(item, "Id"), Transcript = Json.Str(item, "Transcript"),
                            OwnerPid = Convert.ToInt32(item["OwnerPid"]), OwnerTicks = Convert.ToInt64(item["OwnerTicks"]), Offset = Convert.ToInt64(item["Offset"]) };
                        state.Sessions.Add(session);
                        object active;
                        if (item.TryGetValue("Active", out active)) foreach (object id in (IEnumerable)active)
                        { string turn = Convert.ToString(id); state.Tasks.Add(new TaskInfo { SessionId = session.Id, TurnId = turn, RootId = turn }); state.Seen.Add(session.Id + ":" + turn); }
                    }
            }
            Lifecycle.Stop(state, "waiting_new_task"); state.WorkerPid = 0; state.PresencePid = 0; state.HeartbeatTicks = 0;
            return state;
        }

        public static void Install(string source, string root, Settings settings, IEnumerable<string> selectedAccounts)
        {
            ValidateRoot(root);
            var store = new StateStore(root); var mutex = store.Lock();
            try { InstallCore(source, root, settings, selectedAccounts); }
            finally { StateStore.Unlock(mutex); }
        }
        static void InstallCore(string source, string root, Settings settings, IEnumerable<string> selectedAccounts)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); ValidateRoot(root);
            if (!File.Exists(settings.SteamExe) || !File.Exists(settings.VSCodeExe) || !File.Exists(settings.CodexExe)) throw new IOException("Install Steam, VS Code and the native Codex extension first");
            if (!Directory.Exists(settings.CodexHome)) throw new IOException("Open Codex once, or select its existing configuration directory");
            var accounts = selectedAccounts.Distinct().ToArray();
            if (accounts.Length == 0) throw new IOException("Select at least one Steam account");
            var changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var manifestPath = Path.Combine(root, "installation.json");
            Installation manifest = File.Exists(manifestPath) ? Json.Read<Installation>(manifestPath) : new Installation();
            manifest.Root = root; manifest.CodexHome = settings.CodexHome; manifest.Version = "0.1.0";
            string icon = Path.Combine(root, "artwork", "logo.ico");
            var configured = new List<AccountConfig>(settings.Accounts);
            foreach (string account in accounts)
            {
                string path = ShortcutPath(settings, account);
                byte[] before = File.Exists(path) ? File.ReadAllBytes(path) : null;
                var tree = before == null ? new List<VdfEntry>() : Vdf.Read(before);
                var entry = Vdf.EnsureShortcut(tree, root, account, icon);
                configured.RemoveAll(a => a.AccountId == account); configured.Add(entry);
                byte[] after = Vdf.Write(tree);
                if (!Same(before, after)) changes[path] = after;
            }
            if (changes.Count != 0 && SteamPlatform.Inspect(settings).Running) throw new IOException("steam_must_close");
            settings.Accounts = configured; settings.SchemaVersion = 2;
            var files = new List<string> { "CodexSteamBridge.exe", "CodexSteamBridge.Hook.exe", "README.md", "README.en.md", "LICENSE", "COMPATIBILITY.md" };
            foreach (string file in files) if (!File.Exists(Path.Combine(source, file))) throw new FileNotFoundException("Incomplete release package", file);
            bool legacyTask = OwnLegacyTask(root);
            if (legacyTask) RunTaskCommand("/Change /TN CodexSteamBridge /Disable");
            bool success = false;
            try
            {
                StopOwnProcesses(root);
                using (var transaction = new FileTransaction())
                {
                    foreach (string file in files)
                    { transaction.Write(Path.Combine(root, file), File.ReadAllBytes(Path.Combine(source, file))); if (!manifest.Files.Contains(file)) manifest.Files.Add(file); }
                    string sourceArt = Path.Combine(source, "artwork");
                    foreach (string file in Directory.GetFiles(sourceArt))
                    {
                        string relative = Path.Combine("artwork", Path.GetFileName(file));
                        if (!File.Exists(Path.Combine(root, relative)))
                        { transaction.Write(Path.Combine(root, relative), File.ReadAllBytes(file)); if (!manifest.Files.Contains(relative)) manifest.Files.Add(relative); }
                    }
                    foreach (var change in changes) transaction.Write(change.Key, change.Value);
                    foreach (var account in configured.Where(a => accounts.Contains(a.AccountId)))
                    {
                        string config = Path.GetDirectoryName(ShortcutPath(settings, account.AccountId));
                        foreach (var pair in new[] { new[] { "600x900.png", account.AppId + "p.png" }, new[] { "3840x1240.png", account.AppId + "_hero.png" }, new[] { "1280x720.png", account.AppId + "_logo.png" }, new[] { "920x430.png", account.AppId + ".png" } })
                        {
                            string target = Path.Combine(config, "grid", pair[1]);
                            if (File.Exists(target)) continue;
                            byte[] bytes = File.ReadAllBytes(Path.Combine(root, "artwork", pair[0]));
                            transaction.Write(target, bytes); manifest.Assets.Add(new OwnedAsset { Path = target, Hash = Hash(bytes) });
                        }
                    }
                    string hooks = Path.Combine(settings.CodexHome, "hooks.json");
                    transaction.Write(hooks, Hooks.Merge(File.Exists(hooks) ? File.ReadAllBytes(hooks) : null, root, false));
                    transaction.WriteJson(Path.Combine(root, "settings.json"), settings);
                    transaction.WriteJson(Path.Combine(root, "runtime", "state.json"), MigrateState(root));
                    transaction.WriteJson(manifestPath, manifest);
                    transaction.Commit();
                }
                success = true;
                if (legacyTask)
                {
                    RunTaskCommand("/Delete /TN CodexSteamBridge /F");
                    if (OwnLegacyTask(root)) throw new IOException("Cannot remove legacy login task; use the task owner's account to repair");
                }
                foreach (string file in new[] { "stop.watch", "lease.json", "presence.json" })
                { string path = Path.Combine(root, "runtime", file); if (File.Exists(path)) File.Delete(path); }
            }
            finally { if (!success && legacyTask) { RunTaskCommand("/Change /TN CodexSteamBridge /Enable"); RunTaskCommand("/Run /TN CodexSteamBridge"); } }
        }

        public static void Uninstall(string root, bool scheduleCleanup = true)
        {
            ValidateRoot(root);
            var store = new StateStore(root); var mutex = store.Lock();
            try { UninstallCore(root, scheduleCleanup); }
            finally { StateStore.Unlock(mutex); }
        }
        static void UninstallCore(string root, bool scheduleCleanup)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); ValidateRoot(root);
            var store = new StateStore(root); var settings = store.Settings();
            var manifest = Json.Read<Installation>(Path.Combine(root, "installation.json"));
            if (!string.Equals(root, manifest.Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installation manifest mismatch");
            if (SteamPlatform.Inspect(settings).Running) throw new IOException("steam_must_close");
            StopOwnProcesses(root);
            using (var transaction = new FileTransaction())
            {
                foreach (var account in settings.Accounts)
                {
                    string path = ShortcutPath(settings, account.AccountId); if (!File.Exists(path)) continue;
                    var tree = Vdf.Read(File.ReadAllBytes(path)); Vdf.RemoveShortcut(tree, root, new[] { account }); transaction.Write(path, Vdf.Write(tree));
                }
                string hooks = Path.Combine(settings.CodexHome, "hooks.json");
                if (File.Exists(hooks)) transaction.Write(hooks, Hooks.Merge(File.ReadAllBytes(hooks), root, true));
                string config = Path.Combine(settings.CodexHome, "config.toml");
                if (manifest.HookKeys.Count != 0 && File.Exists(config)) transaction.Write(config, Hooks.RemoveTrust(File.ReadAllBytes(config), manifest.HookKeys));
                string userData = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(settings.SteamExe), "userdata")) + Path.DirectorySeparatorChar;
                foreach (var asset in manifest.Assets)
                    if (Path.GetFullPath(asset.Path).StartsWith(userData, StringComparison.OrdinalIgnoreCase) && File.Exists(asset.Path) && Hash(File.ReadAllBytes(asset.Path)) == asset.Hash) transaction.Write(asset.Path, null);
                settings.Enabled = false; transaction.WriteJson(store.SettingsPath, settings);
                transaction.WriteJson(store.StatePath, new BridgeState { Phase = "stopped", Reason = "uninstalled" }); transaction.Commit();
            }
            if (!scheduleCleanup) return;
            // The manager may be running from the installed executable. Delete only the exact owned
            // file list after it exits; never recursively delete a user-selected directory.
            var cleanup = new List<string>(manifest.Files) { "settings.json", "installation.json", Path.Combine("runtime", "state.json") };
            var verified = new List<string>();
            foreach (string relative in cleanup.Distinct())
            {
                string full = Path.GetFullPath(Path.Combine(root, relative));
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe owned file path");
                verified.Add(full);
            }
            string quotedFiles = string.Join(",", verified.Select(p => "'" + p.Replace("'", "''") + "'"));
            string script = "$p=" + Process.GetCurrentProcess().Id + ";$end=[DateTime]::UtcNow.AddSeconds(30);while((Get-Process -Id $p -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $end){Start-Sleep -Milliseconds 250};if(Get-Process -Id $p -ErrorAction SilentlyContinue){exit};foreach($f in @(" + quotedFiles + ")){if(Test-Path -LiteralPath $f -PathType Leaf){Remove-Item -LiteralPath $f -ErrorAction SilentlyContinue}}";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using (var helper = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
        }
    }
}
