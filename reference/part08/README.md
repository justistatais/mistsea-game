# Part 08 — 战斗系统 v0.1

主文档：`Combat_System_v0.1.md`。难度基线：英语Academic读/听/写各6.5；战斗模式独立。数值为设计候选。

阅读顺序：主文档 → `Combat_Change_CR-COM-008-001.md` → `Combat_Validation_v0.1.md` → `Codex_Handoff_Part08_v0.1.md`。

数据在`data/`，新原创文本在`materials/`，前七部分快照在`dependencies/`，有限可执行参考与完整输出在`validation/`。

## 运行

在解压目录执行：

```sh
python validation/run_validation.py
```

建议Python 3.10或更新版本，标准库即可。这个兼容性要求针对本参考代码的语法，不是引擎推荐。脚本生成`run_log.txt`、`validation_report.json`、`traces/`及主验证报告；失败时返回非零。

99项有限检查和27次固定策略场景不是全游戏验收。52项引擎/试玩验收另列；当前未实现与未验证范围见主文档和交接文件。参考模拟不判英语、不发主线经验、不接外部账户，不调用网络模型。

`initial_tower_observation.json`是早期模型历史观察，不能与当前模型混算；`tower_parameter_sweep.json`是使用当前参考策略与援助普攻系数的配对候选比较。保留未过节奏目标的长战结果，不能只报“所有测试胜利”。

首次使用不需要重新运行build_data.py或build_documents.py；它们只用于重新构建设计数据与附录。主文件已生成。

本包没有游戏引擎工程、成品音轨、字体文件或已经标定到6.5的证明。
