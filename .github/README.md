# 雾海档案：第七座灯塔

这是游戏的私有开发仓库，保存 Godot/C# 工程、完整设计、原始资料、双语内容与音频、任务账本和验证记录。当前可运行版本为 Windows 灰盒内部测试版，包含序章、六个地区、终章、微光号船舱、地方生活委托和离线学习入口。

## 运行游戏

在 [2026-10-10 内部版与全量备份](https://github.com/justistatais/mistsea-game/releases/tag/internal-v0.1-backup-20261010) 下载 `MistSea-internal-20261010T121240000513Z.zip`，解压完整目录，双击 `MistSea-internal.exe`。运行库、`.pck` 和资源文件需要保留在同一目录。操作和存档说明见包内 `README_中文.txt`。

这次发行包对应 578 项 C# 测试、29 项原生场景驱动验证，以及实际发行版窗口中的 SQLite 写入冲突、正文导出、保存重试和关闭重开验证。人工语言校准、音频审听与权利审核、96 单元内容冻结及另一台干净 Windows 设备验收仍待完成。在线供应商保持关闭。

## 仓库与完整备份

Git 保存源码、内容资产、原始设计文档、JSON/log 验证记录及任务账本。大体积测试数据库、生成缓存、历史构建和发行二进制保存在同一私有仓库的 Release 附件。完整快照覆盖原游戏目录的每个文件和目录；相同字节内容按 SHA-256 合并存储，恢复时会重新建立原始路径。

- `FULL_SNAPSHOT_MANIFEST.json`：每个原始文件的路径、字节数和 SHA-256，以及全部目录。
- `GITHUB_UPLOAD_MANIFEST.json`：本次备份范围、归档和发行文件的哈希。
- `original_input/`：用户提供的完整设计 ZIP 原件。
- `reports/continuous/delivery_current.author_only.json`：当前可运行版的交付索引。
- `tools/github/restore_snapshot.py`：验证和恢复完整快照的工具。
- `tools/github/join_snapshot.py`：校验并合并完整快照分卷的工具。

根目录 README、`docs/baseline` 和 `reference` 保留原始交接字节。当前开发状态见本页、`planning/state/execution.json` 与实际交付报告。

## 恢复完整游戏目录

从上述 Release 下载全部 `MistSea-complete-snapshot-20261010.zip.partNNN` 分卷、`GITHUB_UPLOAD_MANIFEST.json` 和 `SHA256SUMS.txt`，保存在同一个目录。用 Python 3.12 或更新版本合并分卷，再验证和恢复完整目录：

```powershell
python tools/github/join_snapshot.py "D:\Downloads"
python tools/github/restore_snapshot.py "D:\Downloads\MistSea-complete-snapshot-20261010.zip" --verify-only
python tools/github/restore_snapshot.py "D:\Downloads\MistSea-complete-snapshot-20261010.zip" --destination "D:\MistSea-restored"
```

Release 也附带两个 Python 工具和中文恢复说明，可以直接下载使用。合并工具会校验每个分卷和完整 ZIP 的字节数与 SHA-256。恢复工具自动处理 Windows 长路径，校验全部唯一内容的字节数和 SHA-256，再写入目标目录。目标目录中若存在内容不同的同名文件，恢复会停止；选择一个新的目录即可保留已有工作。

## 开发入口

从 `CODEX_START_HERE.md`、`AGENTS.md` 和当前任务卡读取设计约定。构建方法见 `tools/ci/README.md`；SDK 版本固定在 `global.json`，迁移机器时安装相同版本的 .NET SDK 和 Godot。

```powershell
python tools/ci/build.py test
```

完整历史验证中包含预期失败的故障探针和未通过的旧构建；最终通过记录以交付索引明确指向的那次执行为准。任务的实现、工程验证、人工验收和正式发布状态分别保留在账本中。
