# 战斗参考逻辑检查 v0.1

自动检查：99；失败：0；执行错误：0。

执行27个固定策略场景；结果：{'victory': 27}。这不是人类玩家胜率。

| 配置 | 场景 | 模式 | 结果 | 回合 | 剩余团队生命 | 基础急救使用 | 倒地次数 |
|---|---|---|---|---:|---:|---:|---:|
| BUILD-Q102-ENTRY-H | BENCH-WORKSHOP | standard | victory | 4 | 254/342 | 0 | 0 |
| BUILD-Q102-ENTRY-M | BENCH-WORKSHOP | standard | victory | 4 | 280/342 | 0 | 0 |
| BUILD-Q102-ENTRY-S | BENCH-WORKSHOP | standard | victory | 5 | 258/354 | 0 | 0 |
| BUILD-TOWER-H | BENCH-TOWER | standard | victory | 7 | 197/408 | 1 | 0 |
| BUILD-TOWER-M | BENCH-TOWER | standard | victory | 7 | 191/408 | 1 | 0 |
| BUILD-TOWER-S | BENCH-TOWER | standard | victory | 8 | 201/420 | 1 | 0 |
| BUILD-H | BENCH-ELITE | standard | victory | 7 | 728/1224 | 1 | 0 |
| BUILD-H | BENCH-GROUP | standard | victory | 6 | 1006/1224 | 0 | 0 |
| BUILD-H | BENCH-LONG | standard | victory | 12 | 308/1224 | 2 | 1 |
| BUILD-M | BENCH-ELITE | standard | victory | 7 | 715/1224 | 0 | 0 |
| BUILD-M | BENCH-GROUP | standard | victory | 4 | 989/1224 | 0 | 0 |
| BUILD-M | BENCH-LONG | standard | victory | 11 | 408/1224 | 2 | 1 |
| BUILD-S | BENCH-ELITE | standard | victory | 8 | 780/1272 | 1 | 0 |
| BUILD-S | BENCH-GROUP | standard | victory | 8 | 949/1272 | 0 | 0 |
| BUILD-S | BENCH-LONG | standard | victory | 14 | 239/1272 | 2 | 1 |
| BUILD-HM | BENCH-ELITE | standard | victory | 7 | 766/1248 | 1 | 0 |
| BUILD-HM | BENCH-GROUP | standard | victory | 5 | 1004/1248 | 0 | 0 |
| BUILD-HM | BENCH-LONG | standard | victory | 14 | 69/1248 | 2 | 2 |
| BUILD-S-ATTACKBAR | BENCH-ELITE | standard | victory | 9 | 689/1272 | 1 | 0 |
| BUILD-S-ATTACKBAR | BENCH-GROUP | standard | victory | 6 | 949/1272 | 0 | 0 |
| BUILD-S-ATTACKBAR | BENCH-LONG | standard | victory | 16 | 104/1272 | 2 | 2 |
| BUILD-Q102-ENTRY-H | BENCH-WORKSHOP | relaxed | victory | 3 | 291/342 | 0 | 0 |
| BUILD-Q102-ENTRY-H | BENCH-WORKSHOP | challenge | victory | 5 | 259/342 | 2 | 0 |
| BUILD-Q102-ENTRY-M | BENCH-WORKSHOP | relaxed | victory | 4 | 298/342 | 0 | 0 |
| BUILD-Q102-ENTRY-M | BENCH-WORKSHOP | challenge | victory | 4 | 266/342 | 0 | 0 |
| BUILD-Q102-ENTRY-S | BENCH-WORKSHOP | relaxed | victory | 4 | 283/354 | 0 | 0 |
| BUILD-Q102-ENTRY-S | BENCH-WORKSHOP | challenge | victory | 7 | 233/354 | 2 | 0 |

## 覆盖与限制

99项检查包含30项独立主动效果/费用检查、13项数据契约检查、42项核心边界检查、14项机关/内存回执检查。测试数量不是完整度、平衡度或6.5校准证据。
对比采用第七部分原有合法主修构筑和本轮低等级构筑，固定地方护卫+支援，不装遗物或改件，不携带付费增强补给。高等级示例使用各自登记武器/防具，不能写成全部都是起始装备。
参考演算实现：阵营阶段、稳定次序、目标锁定与列对象、主要主动技能、核心被动、护盾、暴露、压力恢复、装置、免费急救、塔站固定操作点。未实现地图几何/近战遮挡、同伴变体、全部装备触发、普通持续伤害完整计时、完整最终首领、真实存档/渲染。
固定贪心策略不是最优策略；个别动作选择会浪费专注。尤其不能从进攻主动栏在一次脚本中较慢，推出技能本身一定更差。
BENCH-LONG是长战压力台，11—16回合的结果超过主要首领节奏目标；不作为已通过的可发布主线遭遇。保留作为需继续优化的失败节奏指标。
塔站参数扫描使用同一当前策略与援勤普攻0.65A：420生命、32/58/42攻击下巡猎与契印失败；350生命、26/50/34攻击下三配置可胜但契印10回合；310生命下为7/7/8回合，290生命下为6/6/7回合。选310作为候选。旧模型初轮观察另存，不把不同版本的改善解释成单一因素效应。
终章主线保守入口为28级/校准4，而非30级。当前高等级压力台用30级，仅用于比较构筑方向；它没有证明28级终章可通关。
内存快照确定性测试不等于真实磁盘崩溃事务测试。全部引擎与用户试玩要求见COM-AT列表。
