# 第十二部分技术设计包 v0.1

入口：Technical_Architecture_v0.1.md；实施边界：docs/Codex_Handoff_Part12_v0.1.md。

## 本轮实际可运行的内容

Python标准库SQLite参考程序，运行：

```bash
python reference/run_checks.py
```

建议Python3.11以上且SQLite支持JSON校验函数。本轮环境为Python3.13.5 / SQLite3.46.1，脚本只在临时目录创建测试存档，进程终止测试只终止自己的测试子进程。不要将参考程序指向真实游戏存档。

检查结果写入validation/run.log与validation/validation_report.json。参考程序验证了有限事务、分支、草稿、删除记录与内容检查，不实现游戏、导航、战斗、评分或发行签名。

## 并未运行的内容

csharp/是类型化接口草案，本轮没有dotnet与Godot可执行程序，没有编译。工具链选择已用官方页面核验，但实际安装、原生SQLite依赖、真实IME与屏幕阅读器、Windows离线导出均属于T00。schemas/为生产契约草案，reference/使用更小的有限模型，不能声称字段已一比一实现。

sql/001_profile.sql与001_privacy_control.sql是参考程序实际使用的SQL。entities中的payload_json只是受类型约束的存储布局方向；正式游戏必须使用白名单DTO与专用处理器，不能将测试夹具update_entity暴露给UI、AI或内容文件。

upstream_manifest.json记录11份已提供文档的哈希，不把它们全部复制进包。重新解压后检查仍可运行；上游文件不在时不能声称再次核验了上游正文。

本包没有引擎、.NET运行时、字体文件、模型密钥、音轨或用户真实数据。已存在的英文均未改写，6.5目标不变。
