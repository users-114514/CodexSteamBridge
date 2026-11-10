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
        public string Client = "";
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
        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr buffer, int size, out int needed);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CommandLineToArgvW(string command, out int count);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        public static string[] Arguments(Process process)
        {
            IntPtr buffer = Marshal.AllocHGlobal(131072);
            try
            {
                int needed;
                if (NtQueryInformationProcess(process.Handle, 60, buffer, 131072, out needed) != 0) throw new IOException("Cannot identify Codex invocation");
                string command = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, 8), (ushort)Marshal.ReadInt16(buffer) / 2);
                int count; IntPtr values = CommandLineToArgvW(command, out count);
                if (values == IntPtr.Zero) throw new IOException("Cannot parse Codex invocation");
                try { return Enumerable.Range(1, Math.Max(0, count - 1)).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(values, i * IntPtr.Size))).ToArray(); }
                finally { LocalFree(values); }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public static bool SupportedInvocation(string[] arguments)
        {
            var takesValue = new HashSet<string>(new[] { "-c", "--config", "--enable", "--disable", "-m", "--model", "-p", "--profile", "-C", "--cd", "-s", "--sandbox", "-a", "--ask-for-approval", "--add-dir", "-i", "--image", "--local-provider" });
            foreach (string arg in arguments)
                if (arg == "--remote" || arg.StartsWith("--remote=", StringComparison.Ordinal)) return false;
            for (int i = 0; i < arguments.Length; i++)
            {
                string arg = arguments[i];
                if (arg == "--") return true;
                if (takesValue.Contains(arg)) { i++; continue; }
                if (arg.StartsWith("-", StringComparison.Ordinal)) continue;
                // app-server is the actual owner for a local shared daemon or the extension.
                return !new[] { "exec", "e", "review", "agents", "queue", "remote-control", "exec-server", "cloud", "sandbox", "debug" }.Contains(arg);
            }
            return true;
        }

        public static ProcessStamp CodexOwner()
        { return CodexOwner(null); }
        public static ProcessStamp CodexOwner(CodexProfile profile)
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
            string client = "";
            string cli = profile != null && profile.CliEnabled ? CliDiscovery.Resolve(profile.CliEntry) : "";
            for (int i = 0; i < 20; i++)
            {
                ProcessEntry item;
                if (!processes.TryGetValue(current, out item)) break;
                if (string.Equals(item.Name, "codex.exe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (var p = Process.GetProcessById((int)item.Pid))
                        {
                            string image = p.MainModule.FileName;
                            if (profile == null) codex = p.Id;
                            else if (profile.CliEnabled && CliDiscovery.SameImage(image, cli))
                            {
                                codex = p.Id; client = "cli";
                                if (profile.ExtensionEnabled && CliDiscovery.SameImage(image, profile.ExtensionExe) && Arguments(p).FirstOrDefault() == "app-server")
                                {
                                    uint parent = item.Parent;
                                    for (int n = 0; n < 12; n++)
                                    {
                                        ProcessEntry ancestor; if (!processes.TryGetValue(parent, out ancestor)) break;
                                        if (ancestor.Name.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) || ancestor.Name.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)) { client = "vscode"; break; }
                                        parent = ancestor.Parent;
                                    }
                                }
                            }
                            else if (profile.ExtensionEnabled && CliDiscovery.SameImage(image, profile.ExtensionExe)) { codex = p.Id; client = "vscode"; }
                            else return null;
                            if (profile != null)
                            {
                                if (!SupportedInvocation(Arguments(p))) return null;
                                return new ProcessStamp { Pid = p.Id, Ticks = p.StartTime.ToUniversalTime().Ticks, Client = client };
                            }
                        }
                    }
                    catch (System.ComponentModel.Win32Exception) { return null; }
                    catch (InvalidOperationException) { return null; }
                    catch (IOException) { return null; }
                }
                if (profile == null && codex != 0 && (item.Name.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) || item.Name.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase)))
                    using (var p = Process.GetProcessById(codex)) return new ProcessStamp { Pid = p.Id, Ticks = p.StartTime.ToUniversalTime().Ticks, Client = "vscode" };
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
        {
            var settings = new Settings { SteamExe = SteamExe(), VSCodeExe = VSCodeExe(), CodexExe = CodexExe(), CodexHome = CodexHome() };
            string cli = CliDiscovery.Detect();
            settings.CodexProfiles.Add(new CodexProfile { Id = "default", CodexHome = settings.CodexHome, ExtensionExe = settings.CodexExe,
                ExtensionEnabled = File.Exists(settings.CodexExe), CliEntry = cli, CliEnabled = cli.Length != 0 });
            return settings;
        }
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
