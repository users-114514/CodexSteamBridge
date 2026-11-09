using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexSteamBridge
{
    public sealed class ProcessStamp
    {
        public int Pid;
        public long Ticks;
        public static ProcessStamp Current()
        { using (var p = Process.GetCurrentProcess()) return new ProcessStamp { Pid = p.Id, Ticks = p.StartTime.ToUniversalTime().Ticks }; }
        public static bool Alive(int pid, long ticks)
        {
            if (pid <= 0 || ticks <= 0) return false;
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == ticks; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
    }

    public static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ProcessEntry
        {
            public uint Size, Usage, Pid;
            public IntPtr Heap;
            public uint Module, Threads, Parent;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

        public static ProcessStamp CodexOwner()
        {
            var processes = new Dictionary<uint, ProcessEntry>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return null;
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (Process32FirstW(snapshot, ref entry))
                    do { processes[entry.Pid] = entry; } while (Process32NextW(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            uint current = (uint)Process.GetCurrentProcess().Id;
            int codex = 0;
            for (int i = 0; i < 20; i++)
            {
                ProcessEntry item;
                if (!processes.TryGetValue(current, out item)) break;
                if (string.Equals(item.Name, "codex.exe", StringComparison.OrdinalIgnoreCase)) codex = (int)item.Pid;
                if (codex != 0 && (string.Equals(item.Name, "Code.exe", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(item.Name, "Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    try { using (var p = Process.GetProcessById(codex)) return new ProcessStamp { Pid = p.Id, Ticks = p.StartTime.ToUniversalTime().Ticks }; }
                    catch { return null; }
                }
                current = item.Parent;
            }
            return null;
        }
    }

    public static class Discovery
    {
        public static string RegistryString(string path, string name)
        { using (var key = Registry.CurrentUser.OpenSubKey(path)) return key == null ? "" : Convert.ToString(key.GetValue(name, "")); }
        public static string SteamExe()
        {
            string path = RegistryString(@"Software\Valve\Steam", "SteamExe");
            return File.Exists(path) ? Path.GetFullPath(path) : "";
        }
        public static string VSCodeExe()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string path in new[] { Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
                Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "Code - Insiders.exe") })
                if (File.Exists(path)) return path;
            foreach (var process in Process.GetProcessesByName("Code"))
            { using (process) { try { return process.MainModule.FileName; } catch { } } }
            return "";
        }
        public static string CodexExe()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string folder in new[] { ".vscode", ".vscode-insiders" })
            {
                string extensions = Path.Combine(user, folder, "extensions");
                if (!Directory.Exists(extensions)) continue;
                foreach (string extension in Directory.GetDirectories(extensions, "openai.chatgpt-*").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string path = Path.Combine(extension, "bin", "windows-x86_64", "codex.exe");
                    if (File.Exists(path)) return path;
                }
            }
            return "";
        }
        public static string CodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ?
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : configured);
        }
        public static Settings Detect()
        { return new Settings { SteamExe = SteamExe(), VSCodeExe = VSCodeExe(), CodexExe = CodexExe(), CodexHome = CodexHome() }; }
        public static string DefaultRoot
        { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CodexSteamBridge"); } }
        public static List<string> Accounts(string steamExe)
        {
            var result = new List<string>();
            if (!File.Exists(steamExe)) return result;
            string folder = Path.Combine(Path.GetDirectoryName(steamExe), "userdata");
            if (Directory.Exists(folder))
                foreach (string account in Directory.GetDirectories(folder))
                { uint id; if (uint.TryParse(Path.GetFileName(account), out id) && id != 0) result.Add(id.ToString()); }
            return result.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }

    public static class SteamPlatform
    {
        public static SteamObservation Inspect(Settings settings)
        {
            var result = new SteamObservation { Installed = File.Exists(settings.SteamExe) };
            if (!result.Installed) return result;
            // Verify the actual image path, rather than trusting a process with the same name.
            foreach (var process in Process.GetProcessesByName("steam"))
            {
                using (process)
                {
                    try
                    {
                        if (!string.Equals(Path.GetFullPath(process.MainModule.FileName), Path.GetFullPath(settings.SteamExe), StringComparison.OrdinalIgnoreCase)) continue;
                        result.Running = true; result.Pid = process.Id; result.StartTicks = process.StartTime.ToUniversalTime().Ticks;
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                        {
                            if (key != null && Convert.ToInt64(key.GetValue("pid", 0)) == process.Id)
                            {
                                long account = Convert.ToInt64(key.GetValue("ActiveUser", 0));
                                if (account != 0) result.AccountId = unchecked((uint)account).ToString();
                            }
                        }
                        result.Configured = settings.Accounts.Any(a => a.AccountId == result.AccountId && !string.IsNullOrEmpty(a.GameId));
                        return result;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            return result;
        }

        public static bool Launch(Settings settings, BridgeState state)
        {
            var steam = Inspect(settings);
            if (!steam.Running || steam.Pid != state.SteamPid || steam.StartTicks != state.SteamTicks || steam.AccountId != state.AccountId) return false;
            var account = settings.Accounts.SingleOrDefault(a => a.AccountId == steam.AccountId);
            ulong gameId;
            if (account == null || !ulong.TryParse(account.GameId, out gameId) || gameId == 0) return false;
            // -ifrunning is essential: a concurrent Steam exit must not start a new client.
            using (var process = Process.Start(new ProcessStartInfo(settings.SteamExe,
                "-ifrunning -silent -gameidlaunch " + account.GameId) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { }
            return true;
        }
    }
}
