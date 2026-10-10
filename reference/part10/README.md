# 第十部分：UI、交互与学习辅助系统

## 内容

`UI_UX_Spec_v0.1.md`是完整规范；`data/UI_UX_Reference_v0.1.json`登记24种视图、18个共享组件与输入契约；`data/UI_Acceptance_v0.1.json`提供56项正式产品验收；`Codex_UI_Task_Cards_v0.1.md`与对应JSON划分十个开发包。

`prototype/UI_Wireframe_v0.1.html`是单文件、无联网依赖的界面结构示例。它复用第五部分的原始Q-102材料，演示阅读、草稿、多入口回返、帮助、明确结构化方案与简化离港确认。没有真实地图、战斗、语言评分或成品音轨；设备控件明确标注为模拟。

脚本只在本浏览器自己的存储键中保存演示草稿/设置/帮助，不读取项目文件或其他账户。浏览器阻止存储时有保存失败提示与导出。不同文件路径、浏览器或清理浏览数据可能使原演示草稿不可用，正式游戏存档不采用这项行为。

## 检查

参考逻辑仅需Python标准库：

```bash
python validation/run_reference_checks.py
```

浏览器检查需要环境中已经安装Playwright与Chromium，执行方法见`Codex_Handoff_Part10_v0.1.md`，不会自动联网安装。

```bash
python validation/test_browser.py
```

本轮受限浏览器禁止URL导航；检查使用`set_content`以内存加载精确HTML，未测试直接文件打开、localhost导航或真实origin持久化。没有替换浏览器存储为假成功对象；实际测试了存储不可用时的错误提示。

`validation/reference_report.json`、`validation/browser_report.json`与逐项日志是实际执行结果。`UI_UX_Validation_v0.1.md`说明覆盖与未验证范围。`validation/qa_*.png`是当前HTML实际渲染的检查图，不是最终游戏画面。

## 尚需完成

24页完整生产接入、真实世界/存档/战斗服务、原生输入法和读屏、音轨同步、跨实例文件锁、异常关闭与6.5目标人群验收。所有56项正式产品验收仍需要在实际实现中逐项执行。参考模型与DOM检查通过不能代替这些工作。

不包含字体文件，不复制外部商业词典，不生成官方IELTS成绩。正式字体、词库、音轨和AI服务的授权与生产由第十一部分继续确定。

独立解包已重跑52项参考检查与20项浏览器检查，通过；见`validation/independent_recheck.json`。正式引擎、真实origin存储、原生IME、读屏与人群校准仍未验证。
