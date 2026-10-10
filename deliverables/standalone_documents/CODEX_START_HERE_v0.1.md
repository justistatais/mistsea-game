# 给Codex的启动指令
解压完整目录后，从此目录打开工作区；已有仓库先核对冲突，不覆盖文件。下面可直接作为第一条任务消息。

```text
执行 CX-001，仅执行这一项。
先读根目录 AGENTS.md、docs/integration/MASTER_GDD_v0.1.md、
docs/integration/Integration_Decisions_v0.1.md、planning/tasks/CX-001.md。
核对当前工作树、十二份规范、reference目录和执行账，运行本包可用的计划校验工具。
不要开始制作全游戏，不要安装/启用未经明确需要的付费或在线服务，不要覆盖已有用户代码。
将真实执行结果写入 reports/execution/CX-001.json，列出命令、退出码、产物和未执行内容。
遇到缺文件或依赖失败，给出具体blocked项并保留已完成的可检验结果，不虚构完成状态。
完成该项后停止，报告下一项可执行任务及其前置；不要自动推进人工验收。
```

后续消息使用：
```text
按当前执行账实施 <任务ID>。先核对前置和对应任务卡，保持所有基线。
只修改允许范围，执行真实相关测试；记录产物、失败与未验证项，不自动完成后续任务。
```

## 本地查看命令
```bash
python tools/verify_handoff.py
python -m unittest discover -s tools/tests -v
python tools/plan.py status
python tools/plan.py next --limit 10
python tools/plan.py show CX-001
```
这些命令只校验/查看交接计划，不安装依赖、不生成游戏、不把任务标为完成。Python是此计划工具的要求，不是将来发行游戏要求玩家安装的运行环境。

官方Codex文档说明AGENTS.md可用于项目工作约定；实际功能与读取行为以所使用版本为准。官方入口与核对日期见docs/integration/External_References_v0.1.md。
