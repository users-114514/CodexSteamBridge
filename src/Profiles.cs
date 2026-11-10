using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CodexSteamBridge
{
    public sealed class CodexProfile
    {
        public string Id = "";
        public string CodexHome = "";
        public string ExtensionExe = "";
        public string CliEntry = "";
        public bool ExtensionEnabled;
        public bool CliEnabled;
        public bool Enabled = true;
        public bool LegacyHooks;
    }
    public sealed class ProfileIssue { public string ProfileId = ""; public string Reason = ""; }
    public static class Profiles
    {
        public static string Home(string path)
        { return Path.GetFullPath(path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path).TrimEnd(Path.DirectorySeparatorChar); }
        public static Settings Normalize(Settings settings)
        {
            if (settings == null || (settings.SchemaVersion != 2 && settings.SchemaVersion != 3)) throw new InvalidDataException("Unsupported configuration version");
            if (settings.CodexProfiles == null || settings.Accounts == null) throw new InvalidDataException("Invalid configuration collections");
            if (settings.SchemaVersion == 2 && settings.CodexProfiles.Count == 0 && !string.IsNullOrEmpty(settings.CodexHome))
                settings.CodexProfiles.Add(new CodexProfile { Id = "default", CodexHome = settings.CodexHome,
                    ExtensionExe = settings.CodexExe, ExtensionEnabled = !string.IsNullOrEmpty(settings.CodexExe), LegacyHooks = true });
            settings.SchemaVersion = 3;
            return settings;
        }
        public static void Validate(Settings settings)
        {
            Normalize(settings);
            var ids = new HashSet<string>(StringComparer.Ordinal); var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in settings.CodexProfiles)
            {
                if (profile == null || string.IsNullOrEmpty(profile.Id) || profile.Id.Length > 64 || profile.Id.Any(c => !Characters.LetterOrDigit(c) && c != '-' && c != '_'))
                    throw new InvalidDataException("Invalid profile identifier");
                profile.CodexHome = Home(profile.CodexHome);
                if (!ids.Add(profile.Id) || !homes.Add(profile.CodexHome)) throw new InvalidDataException("Duplicate Codex profile or home directory");
                if (profile.LegacyHooks && profile.Id != settings.DefaultProfileId) throw new InvalidDataException("Legacy hooks require the default profile");
                if (!Directory.Exists(profile.CodexHome)) throw new DirectoryNotFoundException("Codex home not found: " + profile.CodexHome);
                if (profile.Enabled && !((profile.ExtensionEnabled && File.Exists(profile.ExtensionExe)) ||
                    (profile.CliEnabled && File.Exists(CliDiscovery.Resolve(profile.CliEntry))))) throw new IOException("No supported Codex client: " + profile.CodexHome);
            }
            if (!settings.CodexProfiles.Any(p => p.Enabled && (p.ExtensionEnabled || p.CliEnabled))) throw new IOException("Enable at least one Codex client");
            if (!ids.Contains(settings.DefaultProfileId)) settings.DefaultProfileId = settings.CodexProfiles.First().Id;
        }
        public static string SessionKey(string profile, string session) { return profile + ":" + session; }
        public static void Issue(BridgeState state, string profile, string reason)
        {
            state.Issues.RemoveAll(i => i.ProfileId == profile);
            if (reason.Length != 0) state.Issues.Add(new ProfileIssue { ProfileId = profile, Reason = reason });
        }
    }
    internal static class Characters
    {
        public static bool LetterOrDigit(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9'; }
    }
    public static class CliDiscovery
    {
        public static string Resolve(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return "";
            entry = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
            if (!Path.IsPathRooted(entry))
            {
                foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                    foreach (string suffix in new[] { "", ".cmd", ".exe", ".ps1" })
                    {
                        string candidate; try { candidate = Path.Combine(directory.Trim('"'), entry + suffix); } catch (ArgumentException) { continue; }
                        if (File.Exists(candidate)) { string found = Resolve(Path.GetFullPath(candidate)); if (found.Length != 0) return found; }
                    }
                return "";
            }
            if (!File.Exists(entry)) return "";
            if (Path.GetExtension(entry).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(entry);
            if (!new[] { ".cmd", ".ps1", "" }.Contains(Path.GetExtension(entry).ToLowerInvariant())) return "";
            // Resolve known npm layouts as data; never execute an arbitrary launcher.
            string directoryRoot = Path.GetDirectoryName(entry);
            string package = Path.Combine(directoryRoot, "node_modules", "@openai", "codex");
            string manifest = Path.Combine(package, "package.json");
            if (!File.Exists(manifest) || Json.Str(Json.Parse(File.ReadAllText(manifest)), "name") != "@openai/codex") return "";
            foreach (string vendor in new[] { Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor"), Path.Combine(package, "vendor") })
            {
                string binary = Path.Combine(vendor, "x86_64-pc-windows-msvc", "bin", "codex.exe");
                if (File.Exists(binary)) return binary;
            }
            return "";
        }
        public static string Detect()
        {
            // Prefer launchers from PATH over VS Code's injected codex.exe.
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                foreach (string name in new[] { "codex.cmd", "codex.ps1", "codex.exe" })
                {
                    if (directory.IndexOf(".vscode", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string path; try { path = Path.Combine(directory.Trim('"'), name); } catch (ArgumentException) { continue; }
                    if (Resolve(path).Length != 0) return path;
                }
            // Prefer an already running standalone client to an inactive old NVM version.
            foreach (var process in Process.GetProcessesByName("codex"))
                using (process)
                {
                    try { string path = process.MainModule.FileName; if (path.IndexOf(".vscode", StringComparison.OrdinalIgnoreCase) < 0 && Resolve(path).Length != 0) return path; }
                    catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
                }
            foreach (string root in new[] { Environment.GetEnvironmentVariable("NVM_HOME") ?? "",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nvm"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm") })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string folder in new[] { root }.Concat(Directory.GetDirectories(root, "v*", SearchOption.TopDirectoryOnly).OrderByDescending(x => x)))
                { string entry = Path.Combine(folder, "codex.cmd"); if (Resolve(entry).Length != 0) return entry; }
            }
            return "";
        }
        public static bool SameImage(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(FinalPath(a), FinalPath(b), StringComparison.OrdinalIgnoreCase);
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, System.Text.StringBuilder path, uint length, uint flags);
        static string FinalPath(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var result = new System.Text.StringBuilder(32768);
                    uint length = GetFinalPathNameByHandle(stream.SafeFileHandle, result, (uint)result.Capacity, 0);
                    if (length > 0 && length < result.Capacity) return Profiles.Home(result.ToString());
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            return Profiles.Home(path);
        }
    }
}
