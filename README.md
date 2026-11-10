# CodexSteamBridge

[English](README.en.md) · Windows x64 · v0.2.0 实验版 · MIT

[下载 Windows 程序包](https://github.com/users-114514/CodexSteamBridge/releases/download/v0.2.0/CodexSteamBridge-v0.2.0-windows-x64.zip) · [发布说明与 SHA-256](https://github.com/users-114514/CodexSteamBridge/releases/tag/v0.2.0)

本机 Codex CLI 或 VS Code Codex 执行任务时，在 Steam 显示“正在玩 Codex”；全部任务结束后退出状态程序。CLI 可独立使用，无需安装 VS Code。

## 安装与升级

1. 解压 Windows 程序包，双击 `CodexSteamBridge.exe`。默认按当前用户安装到 `%LOCALAPPDATA%\Programs\CodexSteamBridge`。首次创建、修改 Steam 库条目及卸载前，请自行关闭 Steam；工具不会关闭或重启 Steam。
2. 检查 Steam、账号和“Codex 配置目录”。可添加多个目录，分别配置扩展程序和 CLI 入口、启用或停用客户端；点击“安装 / 修复”统一保存。CLI 入口可选择 `codex.exe` 或 npm / NVM 的 `codex.cmd`、`codex.ps1`。
3. 在对应目录的 CLI 输入 `/hooks`，或在扩展原生界面“审核钩子 / Review hooks”，审核本工具的 7 项通知；重启 CLI 会话或重新加载 VS Code，再点击“检查状态”确认授权并提交任务。工具不会代替授权。

通知 = Codex 在提交、结束、取消任务时调用的本地命令。无需额外 API 密钥；Codex 本身须正常登录和工作。

从 v0.1.1 升级：运行新包，选择原安装目录并点击“安装 / 修复”。旧目录迁入默认配置，原无参数 `event` 命令及未变更的授权保留，账号、库条目和图片保留；清除旧运行状态，等待下一次新任务。新增目录使用 `event --profile <编号>`，须另行原生审核。多目录配置变更失败会整体回滚文件。

## 启停规则

| 场景 | 行为 |
| --- | --- |
| 登录电脑、打开编辑器、终端或 CLI、选择旧会话、等待下一条输入 | 不启动联动后台 |
| 交互式 `codex`、`codex resume`、`codex fork` 中提交新任务 | Steam 已运行且账号已配置时，单次静默启动 |
| 等待审批或用户回答 | 任务未结束，保持联动 |
| 关闭终端 | 实际执行进程仍工作时继续；执行进程退出后结束该进程的任务 |
| 多窗口、CLI 与扩展、多配置目录并发 | 共享一个工具任务后台和一个 Steam 状态进程 |
| 全部任务完成、取消或失败 | 结束状态并退出后台 |
| Steam 未安装或未运行 | 直接退出，不启动 Steam；本任务不重试、空闲不探查 |
| Steam 已运行但未登录 | 最多等待 60 秒；任务提前完成即退出 |
| 账号未配置、Steam 关闭、重启、退出登录或切换账号 | 退出本次联动，下次新任务重新检查 |
| 手动停止或工具崩溃 | 当前任务不自动拉起；下一次新用户提交允许启动 |
| Steam 点击“开始游戏” | 核对当前任务并恢复；无任务退出，不打开编辑器或创建任务 |

自动续跑、子任务及重复通知不作为新用户任务。CLI 在 VS Code 内置终端运行时仍按 CLI 处理。每次新任务重新解析已登记 CLI 启动入口，兼容 npm / NVM 链接和更新；手动填写的固定旧版本目录被删除时，需在管理界面重新识别或修正。

启动确认上限 10 秒，不重复请求；检查间隔 250 毫秒；后台存活信号失联 4 秒后状态程序退出。正常任务结束后本地退出目标 2 秒内，好友端显示延迟由 Steam 决定。遇到未知来源或任务格式，仅停止计入该会话并显示原因，其他正常会话继续。

## 管理与隐私

重启电脑后无需手动开启工具；已授权通知在下一次新任务时按需启动。管理界面用于检查、修复、启停、添加或移除配置目录及卸载。新 Steam 账号须单独配置库条目。安装只合并本工具配置，卸载只移除登记目录内本工具拥有的通知与授权、库条目及未被修改的发行图片。

无开机自启、日志、遥测、自动更新。不保存提示词、正文、截图或事件历史；仅保存配置编号、任务去重标识、读取位置、进程身份及当前异常原因。读取 Codex 自己维护的已登记会话增量记录时，正文只经过内存。公开图片为本项目原创，本机原有图片不会覆盖。

## 开发与边界

Windows x64、.NET Framework 4.8、C# / Windows Forms；无需额外 Node.js、Python 或 .NET 安装包运行工具。

```powershell
.\Build.ps1
.\Test.ps1
.\Package.ps1
```

`src` 为源码，`tests` 为隔离样例及真实进程测试；自动测试无需模型账号。GitHub Actions 在干净检出中构建、测试并从标签发布。实测版本与未验证项目见 [COMPATIBILITY.md](COMPATIBILITY.md)，升级说明见 [CHANGELOG.md](CHANGELOG.md)。

支持 Windows 本机交互式客户端；不覆盖 `exec` / `review` 自动化、WSL、SSH / 远程、容器、macOS、Linux 或 ARM64。项目独立于 OpenAI、Valve、Microsoft；MIT 不授予第三方商标权。
