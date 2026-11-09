# CodexSteamBridge

[English](README.en.md) · Windows x64 · v0.1.1 实验版 · MIT

[下载 Windows 程序包](https://github.com/users-114514/CodexSteamBridge/releases/download/v0.1.1/CodexSteamBridge-v0.1.1-windows-x64.zip) · [发布说明与校验值](https://github.com/users-114514/CodexSteamBridge/releases/tag/v0.1.1)

在本机 VS Code 的 Codex 执行任务时，使用 Steam 非 Steam 游戏条目显示“正在玩 Codex”，所有任务结束后退出状态程序。

## 安装

1. 下载发行包并解压，双击 `CodexSteamBridge.exe`。默认安装到当前用户的 `%LOCALAPPDATA%\Programs\CodexSteamBridge`，可更改路径。首次安装、修改库条目或卸载前，请自行关闭 Steam；程序不会关闭或重启 Steam，也不会恢复启动其他游戏。
2. 检查自动识别的程序、Codex 配置目录和 Steam 账号，点击“安装 / 修复”。已有本工具安装会被识别并原地升级，保留现有库条目编号、名称和图片。
3. 在 VS Code 的 Codex 中选择“审核钩子 / Review hooks”，信任本工具的 7 个通知入口；重新加载已打开的 VS Code 窗口，返回管理界面点击“检查状态”，再提交新任务。程序不会代替用户信任通知入口。

“钩子”是 Codex 在提交、结束、取消任务时调用的本地通知命令。不需要 OpenAI API 密钥，不修改项目代码。

从 v0.1.0 升级：解压 v0.1.1，运行新包中的 `CodexSteamBridge.exe`，选择原安装目录并点击“安装 / 修复”；完成后在 VS Code 执行“开发人员：重新加载窗口”，再提交新任务。升级保留账号、库条目、图片及未变更通知的原有授权；可在管理界面点击“检查状态”确认。

## 启停规则

| 情况 | 行为 |
| --- | --- |
| Windows 登录、打开编辑器或 Codex 面板 | 无后台自启 |
| 新任务，Steam 已运行且账号已配置 | 单次静默启动，无“打开游戏”弹窗 |
| Steam 未安装、未运行、账号未配置 | 直接退出；本任务无重试、无后台探查，下次新任务重新检查 |
| Steam 已运行但未登录 | 等待最多 60 秒；任务提前结束即退出 |
| 全部任务结束、取消、失败 | 状态程序及任务后台退出 |
| 手动停止、工具崩溃、Steam 重启或切换账号 | 本次不自动恢复，下次新任务再检查 |
| Steam 中点击“开始游戏” | 有当前任务则恢复联动，无任务则退出；不打开 VS Code、不创建任务 |

启动确认只等待 10 秒，不重发启动请求。后台失联 4 秒后，状态程序自行退出。任务记录检查间隔 250 毫秒；程序退出目标为任务完成后 2 秒内，Steam 好友端传播延迟由 Steam 决定。等待用户回答或批准仍属于未结束任务。

## 管理与隐私

再次双击安装目录中的程序可检查状态、修复、启用／停用或卸载。新增 Steam 账号后，需要关闭 Steam 并在管理界面为该账号安装库条目。安装、升级和修复只合并本工具通知项；卸载只删除本工具拥有的条目与未被修改的发行图片。

没有日志、遥测、后台下载或自动更新。不保存提示词、对话正文、截图或事件历史；仅在用户目录保存运行配置，以及任务标识、记录读取位置、进程存活信息和去重标识。为判断真实完成边界，会在内存中读取已登记会话的增量记录。

## 开发

Windows x64、.NET Framework 4.8；无需 Python、Node.js 或额外运行时。

```powershell
.\Build.ps1
.\Test.ps1
.\Package.ps1
```

工程使用系统 C# 编译器、Windows Forms 和框架类库。`src` 包含源码，`tests` 包含隔离验证。GitHub Actions 构建与测试，版本标签 `v0.1.1` 生成发行附件。兼容边界及实测状态见 [COMPATIBILITY.md](COMPATIBILITY.md)。

项目独立于 OpenAI、Valve 和 Microsoft；发行图片由本项目原创绘制，MIT 许可证不授予第三方商标权。
