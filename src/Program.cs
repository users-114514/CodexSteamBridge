using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Runtime.Versioning;
using System.Reflection;

[assembly: AssemblyTitle("CodexSteamBridge")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace CodexSteamBridge
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
            string mode = args.Length == 0 ? "manage" : args[0];
            try
            {
                string root = BridgeRuntime.ExecutableRoot;
                var runtime = new BridgeRuntime(root);
                if (mode == "event")
                {
                    // Hooks must never block or steer a Codex task. Input is consumed in memory only.
                    string input = Console.In.ReadToEnd();
                    if (input.Length <= 4 * 1024 * 1024) runtime.Receive(Json.Parse(input));
                    Console.Write("{}"); return 0;
                }
                if (mode == "run") { runtime.Run(); return 0; }
                if (mode == "presence") { runtime.Presence(); return 0; }
                if (mode == "status") { Console.Write(Json.Serializer().Serialize(new StateStore(root).Load())); return 0; }
                if (mode != "manage") return 2;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new Manager()); return 0;
            }
            catch (Exception error)
            {
                if (mode == "event") { Console.Write("{}"); return 0; }
                if (mode == "manage") MessageBox.Show(error.Message, "CodexSteamBridge");
                return 1;
            }
        }
    }
}
