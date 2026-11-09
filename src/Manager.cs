using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexSteamBridge
{
    public sealed class Manager : Form
    {
        readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        readonly CheckedListBox accounts = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Height = 76 };
        readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(680, 0), Padding = new Padding(4) };
        readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        readonly FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        readonly TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 3, Padding = new Padding(14) };
        readonly List<Control> translated = new List<Control>();
        Settings detected;
        bool busy;
        bool trusted;
        bool English { get { return language.SelectedIndex == 1; } }
        string T(string zh, string en) { return English ? en : zh; }
        string Root { get { return fields["root"].Text.Trim(); } }
        void Caption(Control control, string zh, string en) { control.Tag = new[] { zh, en }; translated.Add(control); control.Text = T(zh, en); }

        public Manager()
        {
            Text = "CodexSteamBridge 0.1.0 — Experimental"; MinimumSize = new Size(760, 650); Size = new Size(840, 720);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); Controls.Add(table);
            language.Items.AddRange(new[] { "简体中文", "English" }); language.SelectedIndex = 0;
            table.Controls.Add(new Label { Text = "Language / 语言", AutoSize = true }, 0, 0); table.Controls.Add(language, 1, 0);
            language.SelectedIndexChanged += (s, e) => { foreach (var control in translated) { var names = (string[])control.Tag; control.Text = T(names[0], names[1]); } RefreshStatus(); };
            AddPath("root", "安装目录", "Install directory", true, 1);
            AddPath("steam", "Steam 程序", "Steam executable", false, 2);
            AddPath("vscode", "VS Code 程序", "VS Code executable", false, 3);
            AddPath("codex", "Codex 程序", "Codex executable", false, 4);
            AddPath("home", "Codex 配置目录", "Codex home", true, 5);
            var label = new Label { AutoSize = true }; Caption(label, "添加/修复所选账号", "Add/repair accounts"); table.Controls.Add(label, 0, 6); table.Controls.Add(accounts, 1, 6);
            var scan = new Button { AutoSize = true }; Caption(scan, "识别", "Detect"); scan.Click += (s, e) => ScanAccounts(); table.Controls.Add(scan, 2, 6);
            table.Controls.Add(buttons, 0, 7); table.SetColumnSpan(buttons, 3);
            AddButton("安装 / 修复", "Install / repair", async () => await Install());
            AddButton("检查状态", "Check status", async () => await CheckTrust());
            AddButton("审核指引", "Review instructions", () =>
            {
                var settings = ReadFields();
                if (!File.Exists(settings.VSCodeExe)) throw new IOException(T("未找到 VS Code", "VS Code not found"));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(settings.VSCodeExe) { UseShellExecute = true });
                MessageBox.Show(this, T("在 VS Code 的 Codex 输入区点击“审核钩子 / Review hooks”，核对并信任本工具的 7 项通知入口，然后返回点击“检查状态”。", "In the Codex composer, select Review hooks and trust this tool's seven handlers, then return and select Check status."));
                return Task.FromResult(0);
            });
            AddButton("启用 / 停用", "Enable / disable", () =>
            {
                var store = new StateStore(Root); var settings = store.Settings(); settings.Enabled = !settings.Enabled; Json.Write(store.SettingsPath, settings);
                if (!settings.Enabled)
                {
                    var mutex = store.Lock();
                    try { var state = store.Load(); Lifecycle.Stop(state, "disabled"); store.Save(state); }
                    finally { StateStore.Unlock(mutex); }
                }
                RefreshStatus(); return Task.FromResult(0);
            });
            AddButton("卸载", "Uninstall", async () =>
            {
                if (MessageBox.Show(this, T("请先关闭 Steam。卸载将移除本工具配置和库条目，并保留其他程序及自定义文件。继续？", "Close Steam first. Remove this tool's integration and library entries, preserving other programs and custom files?"), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                await Task.Run(() => Installer.Uninstall(Root));
                MessageBox.Show(this, T("卸载完成；关闭此窗口后删除程序文件。", "Uninstalled. Owned program files will be removed after this window closes.")); Close();
            });
            table.Controls.Add(status, 0, 8); table.SetColumnSpan(status, 3);
            var policy = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(4, 12, 4, 4) };
            Caption(policy, "按需启动：仅新任务或 Steam“开始游戏”触发。Steam 未安装或未运行时直接退出；已运行但未登录时最多等待 60 秒。无日志、无遥测、无开机自启。", "On demand: a new task or Steam Play triggers integration. Missing/stopped Steam ends the attempt. Running but signed-out Steam is given 60 seconds. No logs, telemetry or login startup."); table.Controls.Add(policy, 0, 9); table.SetColumnSpan(policy, 3);
            detected = Discovery.Detect();
            string current = BridgeRuntime.ExecutableRoot;
            string candidate = File.Exists(Path.Combine(current, "settings.json")) ? current : Discovery.DefaultRoot;
            if (!File.Exists(Path.Combine(candidate, "settings.json")) && File.Exists(detected.SteamExe))
            {
                string legacy = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(detected.SteamExe), "..", "CodexSteamBridge"));
                if (File.Exists(Path.Combine(legacy, "settings.json"))) candidate = legacy;
            }
            fields["root"].Text = candidate;
            if (File.Exists(Path.Combine(candidate, "settings.json")))
            {
                try
                {
                    var data = Json.Parse(File.ReadAllText(Path.Combine(candidate, "settings.json")));
                    if (Json.Str(data, "SchemaVersion") == "2") detected = Json.Read<Settings>(Path.Combine(candidate, "settings.json"));
                    else if (File.Exists(Json.Str(data, "SteamExe"))) detected.SteamExe = Json.Str(data, "SteamExe");
                }
                catch { }
            }
            fields["steam"].Text = detected.SteamExe; fields["vscode"].Text = detected.VSCodeExe;
            fields["codex"].Text = detected.CodexExe; fields["home"].Text = detected.CodexHome;
            if (detected.Language == "en") language.SelectedIndex = 1;
            ScanAccounts(); RefreshStatus();
        }
        void AddPath(string key, string zh, string en, bool folder, int row)
        {
            var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left }; Caption(label, zh, en); table.Controls.Add(label, 0, row);
            var input = new TextBox { Dock = DockStyle.Fill }; fields[key] = input; table.Controls.Add(input, 1, row);
            var browse = new Button { AutoSize = true }; Caption(browse, "选择", "Browse"); table.Controls.Add(browse, 2, row);
            browse.Click += (s, e) =>
            {
                if (folder) { using (var dialog = new FolderBrowserDialog()) { dialog.SelectedPath = input.Text; if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath; } }
                else { using (var dialog = new OpenFileDialog { Filter = "Windows executable (*.exe)|*.exe", FileName = input.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.FileName; }
            };
        }
        void AddButton(string zh, string en, Func<Task> action)
        {
            var button = new Button { AutoSize = true, Margin = new Padding(4, 12, 4, 4) }; Caption(button, zh, en); buttons.Controls.Add(button);
            button.Click += async (s, e) =>
            {
                if (busy) return; busy = true; buttons.Enabled = false;
                try { await action(); }
                catch (Exception error) { MessageBox.Show(this, error.Message == "steam_must_close" ? T("请先自行关闭 Steam，再点击安装、修复或卸载；本工具不会关闭或重启 Steam。", "Close Steam yourself, then retry. This tool does not close or restart Steam.") : error.Message, Text); }
                finally { busy = false; if (!IsDisposed) buttons.Enabled = true; }
            };
        }
        Settings ReadFields()
        {
            var settings = new Settings { SteamExe = fields["steam"].Text.Trim(), VSCodeExe = fields["vscode"].Text.Trim(),
                CodexExe = fields["codex"].Text.Trim(), CodexHome = fields["home"].Text.Trim(), Language = English ? "en" : "zh-CN" };
            if (File.Exists(Path.Combine(Root, "settings.json")))
            { try { var previous = new StateStore(Root).Settings(); settings.Accounts = previous.Accounts; settings.Enabled = previous.Enabled; } catch { } }
            return settings;
        }
        void ScanAccounts()
        {
            accounts.Items.Clear();
            var settings = ReadFields(); string active = SteamPlatform.Inspect(settings).AccountId;
            foreach (string account in Discovery.Accounts(settings.SteamExe)) accounts.Items.Add(account, account == active || settings.Accounts.Any(a => a.AccountId == account));
            if (accounts.CheckedItems.Count == 0 && accounts.Items.Count > 0) accounts.SetItemChecked(0, true);
        }
        async Task Install()
        {
            var settings = ReadFields(); string root = Root; var selected = accounts.CheckedItems.Cast<string>().ToArray();
            await Task.Run(() => Installer.Install(BridgeRuntime.ExecutableRoot, root, settings, selected));
            MessageBox.Show(this, T("安装完成。首次安装或通知定义变更后，请在 Codex 中审核钩子，并重新加载已有 VS Code 窗口；随后提交新任务。", "Installed. Review changed hooks in Codex and reload existing VS Code windows, then submit a new task."));
            await CheckTrust();
        }
        async Task CheckTrust()
        {
            var root = Root; var store = new StateStore(root); var settings = store.Settings();
            var hooks = await Task.Run(() => TrustProbe.Inspect(settings, root));
            trusted = hooks.Count == 7 && hooks.All(h => Json.Str(h, "trustStatus") == "trusted" && Json.Str(h, "enabled").Equals("True", StringComparison.OrdinalIgnoreCase));
            string manifestPath = Path.Combine(root, "installation.json");
            if (File.Exists(manifestPath)) { var manifest = Json.Read<Installation>(manifestPath); manifest.HookKeys = hooks.Select(h => Json.Str(h, "key")).ToList(); Json.Write(manifestPath, manifest); }
            RefreshStatus();
        }
        void RefreshStatus()
        {
            try
            {
                var store = new StateStore(Root);
                if (!File.Exists(store.SettingsPath)) { status.Text = T("尚未安装。选择账号后点击安装。", "Not installed. Select an account and install."); return; }
                var settings = store.Settings(); BridgeState state;
                var mutex = store.Lock();
                try
                {
                    state = store.Load();
                    if (!ProcessStamp.Alive(state.WorkerPid, state.WorkerTicks))
                    {
                        try { TranscriptAdapter.Reconcile(state); }
                        catch (InvalidDataException) { Lifecycle.Stop(state, "unsupported_transcript"); }
                        catch (IOException) { Lifecycle.Stop(state, "transcript_unavailable"); }
                        if (state.Desired) Lifecycle.Stop(state, "runtime_stopped");
                        store.Save(state);
                    }
                }
                finally { StateStore.Unlock(mutex); }
                status.Text = T("联动：", "Integration: ") + (settings.Enabled ? T("已启用", "enabled") : T("已停用", "disabled")) +
                    "\n" + (trusted ? T("Codex 通知：审核通过", "Codex handlers: trusted") : T("Codex 通知：请点击检查状态，确认审核", "Codex handlers: select Check status to verify trust")) +
                    "\n" + T("当前状态：", "Current state: ") + Reason(state.Reason, English) + T("；活动项：", "; active items: ") + state.ActiveCount;
            }
            catch { status.Text = T("检测到旧版或未完成配置，请安装 / 修复。", "Legacy or incomplete configuration. Install / repair required."); }
        }
        public static string Reason(string code, bool en)
        {
            var zh = new Dictionary<string, string> { { "idle", "空闲" }, { "active", "正在联动" }, { "waiting_new_task", "等待下一次新任务" }, { "steam_missing", "未安装 Steam，本次已退出" }, { "steam_not_running", "Steam 未运行，本次已退出" }, { "account_not_configured", "当前 Steam 账号尚未配置" }, { "waiting_presence", "等待 Steam 状态程序启动" }, { "login_timeout", "等待登录 60 秒超时，已退出" }, { "launch_timeout", "启动确认 10 秒超时，已退出" }, { "presence_stopped", "状态程序停止，等待新任务" }, { "account_changed", "Steam 账号已变更，等待新任务" }, { "steam_restarted", "Steam 进程已变更，等待新任务" }, { "disabled", "已停用" }, { "unsupported_transcript", "任务记录格式不受支持" }, { "transcript_unavailable", "任务记录不可用" }, { "runtime_failed", "运行异常，等待新任务" }, { "launch_failed", "Steam 状态程序启动失败" } };
            string value; return !en && zh.TryGetValue(code, out value) ? value : code.Replace('_', ' ');
        }
    }
}
