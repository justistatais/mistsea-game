# 外部核验来源
核验日期：2026-10-09。只采用相关软件项目或官方文档；所选版本未在本轮组成实际Godot运行环境。

| 编号 | 来源 | 支持本文件中的哪类事实 |
|---|---|---|
| S1 | Godot 4.7.2官方发行归档，2026-08-18 | 存在对应.NET编辑器与导出模板 |
| S2 | Godot C# basics与.NET SDK NuGet页 | C#需.NET编辑器；SDK配套版本 |
| S3 | .NET 10官方下载与支持策略 | SDK10.0.401、.NET10 LTS；不是测试兼容性证明 |
| S4 | Microsoft.Data.Sqlite包与事务文档 | SQLite提供者、事务接口 |
| S5 | Microsoft.Data.Sqlite异步限制 | async方法并不提供真实SQLite异步I/O，不在UI线程做阻塞写入 |
| S6 | SQLite Transaction、Atomic Commit | BEGIN IMMEDIATE、单写者和原子提交；存在操作系统与硬件假设 |
| S7 | SQLite Backup API、Microsoft在线备份 | 一致备份使用受支持API，不复制正在写的数据库裸文件 |
| S8 | Godot thread-safe APIs | 活跃场景树不能任意从工作线程修改 |
| S9 | Godot TextEdit | 原生编辑控件可作为候选；真实IME仍要实测 |
| S10 | Godot data paths | res://与user://分工；系统原生路径由适配器解析 |
| S11 | Godot Windows export | x86_64导出、独立资源文件与签名；干净机器仍需验证 |

精确URL在data/sources.json。引擎、SDK、提供者与底层SQLite共同通过T00后，才可称为本项目已验收的工具链。Python环境的SQLite版本不是正式Windows包的版本。
