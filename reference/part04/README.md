# 第四部分：任务与事件系统设计包 v0.1

入口：`Quest_Event_System_v0.1.md`。

这是设计与逻辑检查包，不是已经可游玩的游戏，不包含完整英语素材、运行时语言评估、正式战斗或引擎存档实现。

## 阅读顺序

先阅读主文档第1—7节规则，再看Q-102样例，第13—19节处理大型任务和事件，第21—23节用于实现与验收。附录登记32个顶层主线容器和8个事件模板。

`data/Quest_Event_Reference_v0.1.json`用于引用校验与实现讨论；其中除Q-102外的任务仍是流程契约，不能当作全部已写完的任务数据。

## 运行逻辑检查

在本目录中使用Python 3.10或以上版本运行，仅使用标准库：

```sh
python validation/validate_design.py
```

输出会重写`validation/validation_summary.json`。需要重建参考登记时执行：

```sh
python validation/build_reference.py
python validation/validate_design.py
```

`reference_model.py`只检查选定语义。在内存中的提交示例不等于真实磁盘原子性；自动检查不等于全部40项产品验收完成。

## 文件与依赖

- 主文档：规则、实例、工作单、调度、奖励、恢复与验收。
- `data/`：任务、事件和材料热点参考。
- `validation/`：参考模型、检查脚本、实际运行摘要和日志。
- `dependencies/`：前三份设计及第三部分场景参考的原样副本；本轮不改写它们。
- `Codex_Handoff_Part04_v0.1.md`：本部分交接与禁止越界事项。
- `MANIFEST.json`：文件大小与SHA-256校验值（不包含manifest自身）。

后续第五部分完成英语学习契约，第六部分细化谜题，第七/八部分确定成长与战斗，第十部分UI，第十二部分正式技术架构；第十三部分再统一分派Codex实现任务。
