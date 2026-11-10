using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexSteamBridge
{
    public sealed class Manager : Form
    {
        readonly TextBox root = new TextBox(), steam = new TextBox(), editor = new TextBox();
        readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckedListBox accounts = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        readonly ListBox profiles = new ListBox { Dock = DockStyle.Fill };
        readonly TextBox home = new TextBox(), extension = new TextBox(), cli = new TextBox();
        readonly CheckBox enabled = new CheckBox(), useExtension = new CheckBox(), useCli = new CheckBox();
        readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(920, 0), Padding = new Padding(8) };
        readonly FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        readonly List<Control> translated = new List<Control>();
        Settings draft;
        CodexProfile selected;
        bool loading, busy;
        string trust = "";
        bool English { get { return language.SelectedIndex == 1; } }
        string T(string zh, string en) { return English ? en : zh; }
        void Texts(Control control, string zh, string en) { control.Tag = new[] { zh, en }; translated.Add(control); control.Text = T(zh, en); }
        static TableLayoutPanel Table() { return new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 3, Padding = new Padding(10), AutoSize = true }; }
        public Manager()
        {
            Text = "CodexSteamBridge 0.2.0 — Experimental"; MinimumSize = new Size(900, 740); Size = new Size(1000, 860);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210)); Controls.Add(layout);
            language.Items.AddRange(new[] { "简体中文", "English" }); language.SelectedIndex = 0; layout.Controls.Add(language, 0, 0);
            language.SelectedIndexChanged += (s, e) => { foreach (var control in translated) { var text = (string[])control.Tag; control.Text = T(text[0], text[1]); } RefreshStatus(); };
            var tabs = new TabControl { Dock = DockStyle.Fill }; layout.Controls.Add(tabs, 0, 1);
            var general = new TabPage(); Texts(general, "安装与 Steam", "Installation and Steam"); tabs.TabPages.Add(general);
            var common = Table(); general.Controls.Add(common);
            PathRow(common, 0, "安装目录", "Install directory", root, true);
            PathRow(common, 1, "Steam 程序", "Steam executable", steam, false);
            PathRow(common, 2, "VS Code（可选）", "VS Code (optional)", editor, false);
            var accountLabel = new Label { AutoSize = true }; Texts(accountLabel, "添加／修复所选账号", "Add / repair accounts"); common.Controls.Add(accountLabel, 0, 3); common.Controls.Add(accounts, 1, 3);
            var scan = new Button { AutoSize = true }; Texts(scan, "识别账号", "Detect accounts"); common.Controls.Add(scan, 2, 3); scan.Click += (s, e) => ScanAccounts();
            var profileTab = new TabPage(); Texts(profileTab, "Codex 配置目录", "Codex profiles"); tabs.TabPages.Add(profileTab);
            var split = new SplitContainer { Size = new Size(940, 500), Dock = DockStyle.Fill, SplitterDistance = 220 }; profileTab.Controls.Add(split);
            var profileList = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            profileList.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); profileList.RowStyles.Add(new RowStyle(SizeType.AutoSize)); split.Panel1.Controls.Add(profileList); profileList.Controls.Add(profiles, 0, 0);
            var profileActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; profileList.Controls.Add(profileActions, 0, 1);
            var add = new Button { AutoSize = true }; Texts(add, "添加", "Add"); profileActions.Controls.Add(add);
            var remove = new Button { AutoSize = true }; Texts(remove, "移除", "Remove"); profileActions.Controls.Add(remove);
            add.Click += (s, e) => { SaveProfile(); var p = new CodexProfile { Id = Guid.NewGuid().ToString("N"), ExtensionExe = Discovery.CodexExe(), CliEntry = CliDiscovery.Detect() }; p.ExtensionEnabled = File.Exists(p.ExtensionExe); p.CliEnabled = p.CliEntry.Length != 0; draft.CodexProfiles.Add(p); ReloadProfiles(p.Id); };
            remove.Click += (s, e) => { if (selected == null) return; var id = selected.Id; draft.CodexProfiles.RemoveAll(p => p.Id == id); selected = null; ReloadProfiles(""); };
            var detail = Table(); split.Panel2.Controls.Add(detail);
            PathRow(detail, 0, "配置目录", "Codex home", home, true); PathRow(detail, 1, "扩展 Codex 程序", "Extension executable", extension, false);
            PathRow(detail, 2, "CLI 启动入口", "CLI entry", cli, false);
            Texts(enabled, "启用此目录", "Enable profile"); Texts(useExtension, "联动 VS Code 扩展", "Enable VS Code integration"); Texts(useCli, "联动交互式 CLI", "Enable interactive CLI integration");
            detail.Controls.Add(enabled, 1, 3); detail.Controls.Add(useExtension, 1, 4); detail.Controls.Add(useCli, 1, 5);
            var detect = new Button { AutoSize = true }; Texts(detect, "识别客户端", "Detect clients"); detail.Controls.Add(detect, 1, 6);
            detect.Click += (s, e) => { extension.Text = Discovery.CodexExe(); cli.Text = CliDiscovery.Detect(); useExtension.Checked = File.Exists(extension.Text); useCli.Checked = cli.Text.Length != 0; };
            var hint = new Label { AutoSize = true, MaximumSize = new Size(560, 0) }; Texts(hint, "目录与开关修改后，点击“安装 / 修复”保存。新目录须在 CLI /hooks 或扩展界面审核 7 项通知；随后重新启动 CLI 或重新加载 VS Code。", "Choose Install / repair to save profile changes. Review seven handlers in CLI /hooks or the extension, then restart CLI or reload VS Code."); detail.Controls.Add(hint, 1, 7);
            profiles.SelectedIndexChanged += (s, e) => { if (loading) return; SaveProfile(); selected = profiles.SelectedIndex >= 0 ? draft.CodexProfiles[profiles.SelectedIndex] : null; ShowProfile(); };
            layout.Controls.Add(actions, 0, 2); var statusPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; statusPanel.Controls.Add(status); layout.Controls.Add(statusPanel, 0, 3);
            Button("安装 / 修复", "Install / repair", async () => { var settings = Read(); string target = root.Text.Trim(); var chosen = accounts.CheckedItems.Cast<string>().ToArray(); await Task.Run(() => Installer.Install(BridgeRuntime.ExecutableRoot, target, settings, chosen)); draft = new StateStore(target).Settings(); ReloadProfiles(draft.DefaultProfileId); MessageBox.Show(this, T("安装完成。请审核新增通知并重启 CLI 会话或重新加载 VS Code，再提交新任务。", "Installed. Review new hooks, restart CLI or reload VS Code, then submit a new task.")); await CheckTrust(); });
            Button("检查状态", "Check status", CheckTrust);
            Button("审核指引", "Review instructions", () => { MessageBox.Show(this, T("在目标 Codex 配置目录对应的 CLI 会话输入 /hooks，或在 VS Code Codex 中选择“审核钩子”，核对本工具的 7 项通知后信任；工具不会代替授权。", "Use /hooks in the CLI for the selected Codex home, or Review hooks in VS Code. Review and trust this tool's seven handlers. The tool never grants trust.")); return Task.FromResult(0); });
            Button("启用 / 停用", "Enable / disable", () => { var store = new StateStore(root.Text.Trim()); var settings = store.Settings(); settings.Enabled = !settings.Enabled; var mutex = store.Lock(); try { Json.Write(store.SettingsPath, settings); if (!settings.Enabled) { var state = store.Load(); Lifecycle.Stop(state, "disabled"); store.Save(state); } } finally { StateStore.Unlock(mutex); } draft.Enabled = settings.Enabled; RefreshStatus(); return Task.FromResult(0); });
            Button("卸载", "Uninstall", async () => { if (MessageBox.Show(this, T("请自行关闭 Steam。将移除本工具在所有登记目录中的配置及库条目，保留其他配置和自定义文件。继续？", "Close Steam first. Remove this tool's integration from all registered homes and Steam accounts, preserving unrelated files?"), Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return; string target = root.Text.Trim(); await Task.Run(() => Installer.Uninstall(target)); MessageBox.Show(this, T("卸载完成，关闭窗口后清理程序文件。", "Uninstalled. Close this window to remove owned program files.")); Close(); });
            draft = Discovery.Detect(); string current = BridgeRuntime.ExecutableRoot;
            root.Text = File.Exists(Path.Combine(current, "settings.json")) ? current : Discovery.DefaultRoot;
            if (!File.Exists(Path.Combine(root.Text, "settings.json")) && File.Exists(draft.SteamExe))
            { string legacy = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(draft.SteamExe), "..", "CodexSteamBridge")); if (File.Exists(Path.Combine(legacy, "settings.json"))) root.Text = legacy; }
            bool migrating = true;
            if (File.Exists(Path.Combine(root.Text, "settings.json")))
            {
                try { migrating = Json.Str(Json.Parse(File.ReadAllText(Path.Combine(root.Text, "settings.json"))), "SchemaVersion") == "2"; draft = new StateStore(root.Text).Settings(); }
                catch (IOException) { } catch (ArgumentException) { }
            }
            Profiles.Normalize(draft);
            if (migrating && draft.CodexProfiles.Count != 0) { var p = draft.CodexProfiles[0]; p.CliEntry = CliDiscovery.Detect(); p.CliEnabled = p.CliEntry.Length != 0; }
            steam.Text = draft.SteamExe; editor.Text = draft.VSCodeExe; language.SelectedIndex = draft.Language == "en" ? 1 : 0;
            ReloadProfiles(draft.DefaultProfileId); ScanAccounts(); RefreshStatus();
        }
        void PathRow(TableLayoutPanel table, int row, string zh, string en, TextBox field, bool folder)
        {
            table.ColumnStyles.Clear(); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
            var label = new Label { AutoSize = true }; Texts(label, zh, en); table.Controls.Add(label, 0, row); field.Dock = DockStyle.Fill; table.Controls.Add(field, 1, row);
            var browse = new Button { AutoSize = true }; Texts(browse, "选择", "Browse"); table.Controls.Add(browse, 2, row);
            browse.Click += (s, e) => { if (folder) { using (var dialog = new FolderBrowserDialog { SelectedPath = field.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath; } else { using (var dialog = new OpenFileDialog { Filter = "Program (*.exe;*.cmd;*.ps1)|*.exe;*.cmd;*.ps1|All files (*.*)|*.*", FileName = field.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName; } };
        }
        void Button(string zh, string en, Func<Task> action)
        {
            var button = new Button { AutoSize = true }; Texts(button, zh, en); actions.Controls.Add(button);
            button.Click += async (s, e) => { if (busy) return; busy = true; actions.Enabled = false;
                try { await action(); } catch (Exception error) { MessageBox.Show(this, error.Message == "steam_must_close" ? T("请先自行关闭 Steam，再安装、修复或卸载。", "Close Steam before changing the library or uninstalling.") : error.Message, Text); }
                finally { busy = false; if (!IsDisposed) actions.Enabled = true; } };
        }
        void SaveProfile()
        {
            if (selected == null) return;
            selected.CodexHome = home.Text.Trim(); selected.ExtensionExe = extension.Text.Trim(); selected.CliEntry = cli.Text.Trim();
            selected.Enabled = enabled.Checked; selected.ExtensionEnabled = useExtension.Checked; selected.CliEnabled = useCli.Checked;
            int index = draft.CodexProfiles.IndexOf(selected);
            if (index >= 0 && index < profiles.Items.Count)
            { loading = true; profiles.Items[index] = selected.CodexHome.Length == 0 ? T("新配置目录", "New profile") : selected.CodexHome; loading = false; }
        }
        void ShowProfile()
        {
            home.Text = selected == null ? "" : selected.CodexHome; extension.Text = selected == null ? "" : selected.ExtensionExe; cli.Text = selected == null ? "" : selected.CliEntry;
            enabled.Checked = selected != null && selected.Enabled; useExtension.Checked = selected != null && selected.ExtensionEnabled; useCli.Checked = selected != null && selected.CliEnabled;
        }
        void ReloadProfiles(string id)
        {
            loading = true; profiles.Items.Clear();
            foreach (var profile in draft.CodexProfiles) profiles.Items.Add(profile.CodexHome.Length == 0 ? T("新配置目录", "New profile") : profile.CodexHome);
            int index = draft.CodexProfiles.FindIndex(p => p.Id == id); profiles.SelectedIndex = index >= 0 ? index : profiles.Items.Count > 0 ? 0 : -1;
            selected = profiles.SelectedIndex < 0 ? null : draft.CodexProfiles[profiles.SelectedIndex]; ShowProfile(); loading = false;
        }
        Settings Read()
        { SaveProfile(); draft.SteamExe = steam.Text.Trim(); draft.VSCodeExe = editor.Text.Trim(); draft.Language = English ? "en" : "zh-CN"; return draft; }
        void ScanAccounts()
        {
            accounts.Items.Clear(); var settings = Read(); string active = SteamPlatform.Inspect(settings).AccountId;
            foreach (string account in Discovery.Accounts(settings.SteamExe)) accounts.Items.Add(account, account == active || settings.Accounts.Any(a => a.AccountId == account));
            if (accounts.CheckedItems.Count == 0 && accounts.Items.Count > 0) accounts.SetItemChecked(0, true);
        }
        async Task CheckTrust()
        {
            string target = root.Text.Trim(); var settings = new StateStore(target).Settings(); var lines = new List<string>();
            foreach (var profile in settings.CodexProfiles)
            {
                foreach (string kind in new[] { "vscode", "cli" })
                {
                    if (!(kind == "cli" ? profile.CliEnabled : profile.ExtensionEnabled)) continue;
                    string exe = kind == "cli" ? CliDiscovery.Resolve(profile.CliEntry) : profile.ExtensionExe;
                    try
                    {
                        var hooks = await Task.Run(() => TrustProbe.Inspect(exe, profile.CodexHome, target));
                        bool ready = hooks.Count == 7 && hooks.All(h => Json.Str(h, "trustStatus") == "trusted" && Json.Str(h, "enabled").Equals("True", StringComparison.OrdinalIgnoreCase));
                        lines.Add(profile.CodexHome + " / " + kind + ": " + (ready ? T("审核通过", "trusted") : T("需要审核通知", "review hooks")));
                    }
                    catch (Exception error) { lines.Add(profile.CodexHome + " / " + kind + ": " + error.Message); }
                }
            }
            trust = string.Join("\n", lines); RefreshStatus();
        }
        void RefreshStatus()
        {
            try
            {
                var store = new StateStore(root.Text.Trim()); if (!File.Exists(store.SettingsPath)) { status.Text = T("尚未安装。配置客户端及账号后点击安装。", "Not installed. Configure clients/accounts, then install."); return; }
                var settings = store.Settings(); var state = store.Load();
                status.Text = T("联动：", "Integration: ") + (settings.Enabled ? T("启用", "enabled") : T("停用", "disabled")) + " / " + Reason(state.Reason, English) + "\n";
                foreach (var profile in settings.CodexProfiles)
                {
                    int count = state.Tasks.Count(t => t.Active && t.SessionId.StartsWith(profile.Id + ":", StringComparison.Ordinal));
                    var issue = state.Issues.FirstOrDefault(i => i.ProfileId == profile.Id);
                    status.Text += profile.CodexHome + " — " + (profile.Enabled ? T("启用", "enabled") : T("停用", "disabled")) + " / " + T("活动任务：", "active tasks: ") + count +
                        " / VS Code: " + (File.Exists(profile.ExtensionExe) ? T("已找到", "found") : T("未找到", "missing")) + " / CLI: " + (CliDiscovery.Resolve(profile.CliEntry).Length != 0 ? T("已找到", "found") : T("未找到", "missing")) +
                        (issue == null ? "" : " / " + Reason(issue.Reason, English)) + "\n";
                }
                status.Text += trust.Length == 0 ? T("点击“检查状态”核对各客户端通知授权。", "Select Check status to verify each client's hook trust.") : trust;
            }
            catch { status.Text = T("旧版或未完成配置，请安装 / 修复。", "Legacy/incomplete installation. Choose Install / repair."); }
        }
        public static string Reason(string code, bool en)
        {
            var zh = new Dictionary<string, string> { { "idle", "空闲" }, { "active", "正在联动" }, { "waiting_new_task", "等待下一次新任务" }, { "steam_missing", "Steam 未安装，本次退出" }, { "steam_not_running", "Steam 未运行，本次退出" }, { "waiting_login", "等待 Steam 登录，最多 60 秒" }, { "account_not_configured", "账号未配置" }, { "waiting_presence", "等待 Steam 状态启动" }, { "login_timeout", "等待登录超时" }, { "launch_timeout", "启动确认超时" }, { "presence_stopped", "手动停止，等待新任务" }, { "unsupported_source", "会话来源不受支持" }, { "unsupported_transcript", "任务记录格式不受支持" }, { "transcript_unavailable", "任务记录不可用" }, { "missing_task_start", "任务开始信息不可用" } };
            string value; return !en && zh.TryGetValue(code, out value) ? value : code.Replace('_', ' ');
        }
    }
}
