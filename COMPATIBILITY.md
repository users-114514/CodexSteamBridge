# Compatibility / 兼容性

| 项目 | 支持范围 / Support |
| --- | --- |
| OS | Windows 10 22H2 x64 / Windows 11 x64；普通当前用户安装 |
| Framework | .NET Framework 4.8 |
| Editor | 本机 VS Code / VS Code Insiders，原生 Windows Codex 扩展 |
| Codex adapter | hook payloads with session_id / turn_id; transcript event_msg task_started / task_complete / turn_aborted |
| Steam | 已安装并运行的 Windows 客户端，按账号配置非 Steam 快捷方式 |
| Excluded | WSL, SSH/remote, containers, Codex CLI, macOS, Linux, ARM64 |

本机参考版本：Windows 10 22H2 build 19045；Codex VS Code 扩展 26.1021.22027；其内置 Codex CLI 0.164.0-alpha.11.1。Windows 11 必须由额外机器或 GitHub Windows runner 完成验证，不能从本机结果推定。

Codex 通知定义使用原生信任审核；管理策略禁用通知时，本工具不会绕过。升级 Codex 后应重新检查联动，任务记录属于不稳定接口；未知 task_* 事件会停止联动并显示不兼容状态。

Official references:

- https://learn.chatgpt.com/docs/hooks.md
- https://developers.openai.com/codex/app-server/

Steam 非 Steam 游戏状态与好友隐私、隐身设置及同时运行的其他游戏有关。本工具不会更改账号在线/隐私设置。
