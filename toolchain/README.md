# MistSea Windows 工具链

CX-002 已在本机安装指定工具，并通过 C# 编译、Godot 场景运行、原生 SQLite 和 Windows 导出启动验证。工具放在当前用户目录，版本与 `planning/toolchain_baseline.json` 一致。

| 组件 | 实装版本 |
| --- | --- |
| Godot .NET 编辑器、MSBuild SDK、导出模板 | 4.7.2 stable |
| .NET SDK / 运行时 | 10.0.401 / 10.0.12 |
| 目标框架 / C# | net10.0 / 12 |
| Microsoft.Data.Sqlite | 10.0.12 |
| 实测原生 SQLite | 3.53.3，x64 |
| 渲染器 / 验证平台 | Compatibility / Windows x86_64 |

Godot.NET.Sdk 通过项目的 `Sdk="Godot.NET.Sdk/4.7.2"` 引入。SQLite 使用中央包版本配置；两个试件的 `packages.lock.json` 均由实际还原生成。

在游戏子文件夹中打开 PowerShell，执行：

```powershell
. .\toolchain\Use-MistSeaToolchain.ps1
dotnet --version
godot --version
godot --editor --path .\spikes\toolchain\GodotSmoke
```

脚本只设置当前 PowerShell 会话的路径和包缓存。安装位置记录在 `installed.windows-x64.json`；下载来源、官方校验值、实装二进制和包哈希记录在 `toolchain.lock.json`。

锁文件分别保存完整包归档的哈希与 NuGet 的 `contentHash`。签名包的内容哈希排除签名信息，两种值按各自规则核对，参见 [NuGet 元数据说明](https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File)。

需要重新验证时执行：

```powershell
python -B -X utf8 .\toolchain\verify_windows.py
```

验证脚本会新建一份带时间戳的日志目录。联网还原和漏洞审计使用官方 NuGet 源；离线检查清空源列表，使用缓存完成锁定还原，并单独验证空缓存时的 NU1100 错误。离线检查关闭在线漏洞审计，联网审计结果另行保留。

本轮已验证 SQLite 提交、回滚、备份及完整性检查，Godot C# 场景运行，以及带 .NET 运行时的 Windows 导出程序启动。首次导出因试件缺少 `.sln` 文件失败，补齐后已重新通过，原始失败日志仍在验收记录中。

验证范围是本机工具链。另一台干净 Windows、原生输入法、读屏、正式游戏存档和真人语言校准仍由后续任务验收；G0 尚未开放。当前场景是工具链试件，正式游戏项目从 CX-006 建立。

完整记录见 `reports/execution/CX-002.json`。工具的官方来源为 [Godot 4.7.2 归档](https://godotengine.org/download/archive/4.7.2-stable/)、[.NET 10 下载](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) 和 [Microsoft.Data.Sqlite 10.0.12](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12)。
