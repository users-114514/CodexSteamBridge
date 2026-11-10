# Compatibility / 兼容性

| 项目 / Item | 实测状态 / Verification |
| --- | --- |
| Windows 10 22H2 x64 build 19045 | 本机真实 CLI、Steam，隔离安装及进程测试 / local CLI + Steam and isolated tests |
| Windows 11 x64 desktop | 目标支持，未在 Windows 11 桌面实测 / target support, desktop not tested |
| GitHub runner | Windows Server 2022 build / isolated tests; does not certify Windows 11 desktop |
| .NET Framework | 4.8, x64, current-user installation |
| Interactive CLI 0.163.0 | 官方 npm 原生包，新建、resume、fork 的真实任务与 Steam 启停；默认及 `--no-daemon` 启动 / official native binary, new/resume/fork smoke tests |
| Interactive CLI 0.167.0 | 本机独立安装，默认启动首条任务与 Steam 启停实测 / installed standalone binary, default startup smoke test |
| VS Code extension 26.1021.22027 | 内置 0.164.0-alpha.11.1；原有真实联动已确认，0.2.0 迁移保留 7 项原生授权 / existing confirmed integration, native review preserved |
| Other CLI / extension versions | 未逐版本实测；格式不兼容时隔离退出 / not certified per version |
| Multiple homes / CLI-only install / npm-NVM layout | 隔离配置、进程及事务测试 / isolated configuration, process and rollback tests |
| Shared daemon / no-daemon / closed terminal | 隔离真实进程链覆盖；各真实版本的完整交互矩阵未全部执行 / fixture process chains; full native matrix pending |
| Standard user / Windows 11 / actual NVM switching | 桌面端到端未全部实测；程序使用 asInvoker / full desktop coverage pending |

支持本机交互式 `codex`、`codex resume`、`codex fork`，独立或 VS Code 内置终端。恢复与分叉的用户任务按相同用户提交标记处理。取消、失败、审批等待、多客户端和目录并发、重复及乱序通知、关闭窗口、崩溃和事务回滚有隔离测试；真实模型会话的所有组合、真实 Steam 退出竞态及好友端传播时间仍需额外人工验收。实验版不宣称这些项目全部通过。

Supported task markers: CLI `user`; extension `composer`, `edit_user_message`, `plan_implementation`. `event_msg` records `task_started` / `task_complete` / `turn_aborted` provide task boundaries. Metadata must identify a registered local `cli` or `vscode` source. Transcript details are unstable interfaces: unknown task records retire the affected session and appear in the manager.

Excluded: automated `exec` / `review`, WSL, SSH / remote, containers, macOS, Linux, ARM64. Managed policy disabling hooks is respected. Steam privacy/invisible settings and another game affect friends' display; the bridge never changes these settings.

Official references:

- https://learn.chatgpt.com/docs/hooks.md
- https://developers.openai.com/codex/cli/reference/
- https://developers.openai.com/codex/app-server/
