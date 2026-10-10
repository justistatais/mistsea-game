# 《雾海档案：第七座灯塔》— 完整设计与 Codex 交接包

版本：v0.1。此目录汇总前13部分的设计、原始部分包、参考资料、验证记录和 Codex 任务卡。

## 从这里开始

- `CODEX_START_HERE.md`：Codex 首次执行说明。
- `AGENTS.md`：Codex 项目级约束。
- `docs/integration/MASTER_GDD_v0.1.md`：整合游戏设计总纲。
- `docs/integration/CODEX_IMPLEMENTATION_PLAN_v0.1.md`：开发计划。
- `planning/tasks/`：136 张任务卡；实际状态和依赖见 `planning/plan.json`。
- `docs/baseline/`：第一至十二部分的详细设计规范。
- `reference/`：各部分参考数据、材料、测试、样例等。

## 附加原件

- `deliverables/standalone_documents/`：此前分别交付的完整 Markdown 规范、验证记录、HTML UI 原型等，保留原文件名。
- `deliverables/original_part_packages/`：第三至第十三部分此前逐项交付的 ZIP 原件。
- `deliverables/build_helpers/`：第十三部分的交接包组装脚本和构建记录，供查证。
- `deliverables/ui_review_screenshots/`：第十部分文字界面的检查截图。

`COLLECTION_MANIFEST.json` 是本汇总包各文件的 SHA-256 清单；不对清单自身进行哈希。

## 状态说明

这是设计与开发交接资料的汇总，不是已经编译或可运行的游戏。雅思难度目标为 Academic 阅读、听力、写作各 6.5；正式材料校准仍需按文档实施。

## 计划校验（需 Python）

```sh
python tools/verify_handoff.py
python -m unittest discover -s tools/tests -v
python tools/plan.py status
python tools/plan.py show CX-001
```
