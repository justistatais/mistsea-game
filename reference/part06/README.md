# 第六部分：解谜系统设计包 v0.1

面向《雾海档案：第七座灯塔》的制作与Codex分步实现。

## 先读什么

1. `dependencies/Difficulty_Baseline_Change_CR-EN-005-001.md`：难度固定为IELTS Academic读听写6.5；旧文档中的“6.5及以上”不再生效。
2. `Puzzle_System_v0.1.md`：规则、九个详设单元、家族、提示、恢复、接口、预算与验收。
3. `materials/Worked_Puzzle_Materials_v0.1.md`：十二份新增局部英文材料/脚本及解法核验。
4. `Codex_Handoff_Part06_v0.1.md`：实现顺序、输入输出、允许改动和禁止事项。
5. `Puzzle_Validation_v0.1.md`：实际运行结果及覆盖边界。

## 文件用途

`data/Puzzle_Reference_v0.1.json`登记8家族、9单元、32父任务绑定、27关键约束和支持规则。
`data/Puzzle_Materials_v0.1.json`保存12份新局部材料；其中3份仅为听力脚本，没有音频成品。
`data/Worked_Logic_Fixtures_v0.1.json`提供声明范围内的固定数据。
`dependencies/`是前五部分及必要JSON的原样副本，不表示这些旧版文本被自动改写。

这些JSON是设计参考，不是最终生产格式。Python是有限逻辑例子，不是完整谜题引擎、世界事务管理器或英语评分器。

## 运行参考检查

需要支持类型联合语法的Python（3.10或更新版本）；仅使用标准库。进入本包目录后执行：

```sh
python validation/validate_package.py
```

报告写入`validation/validation_report.json`，逐项日志写入`validation/run_log.txt`。运行会更新这两个验证输出；`MANIFEST.json`记录的是本次交付快照，重跑后这些输出的哈希可以变化。

检查包括来源引用、目标与场景绑定、语义对象约束、方法回程、配置证书、明确修订、偏序、多条通路、多解装载、有限试验结论和缺线索的反事实候选模型。

## 不要误读这些结果

语言目标固定6.5，但稿件尚未进行目标组实测校准。没有制作声音、实际引擎地图、完整战斗、真实磁盘崩溃恢复或自由英文理解服务。Passing tests不能被写成“全游戏不会卡住”或“素材已被证明达到6.5”。

本轮96个谜题单元是完整版预算；实际详设的是9个，其中Q-603补充货载不是Q-603基础关闭或结局资格门槛。短单元不能替代第五部分的长文、长听力和12个长写作机会。
