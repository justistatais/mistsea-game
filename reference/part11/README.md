# 第十一部分设计包

这是内容生产与AI辅助的设计规格及有限参考，不是游戏工程、在线服务或IELTS评分器。

## 阅读顺序
Content_AI_Pipeline_v0.1.md → data/Production_Jobs_v0.1.json → Codex_Handoff_Part11_v0.1.md → Content_AI_Validation_v0.1.md。

## 自包含检查
Python 3.10+；验证JSON Schema需要requirements.txt中的版本。安装依赖可能需要联网，测试本身不联网。

```sh
python -m pip install -r requirements.txt
python tools/verify_package.py
python validation/run_checks.py --output-dir validation/rerun
```

通过verify_package后再运行可保留初始日志不变。run_checks默认会重写validation目录下报告；--output-dir可另存。测试用到的人工审核人与音轨都是SYNTHETIC夹具，不是真实审核或录音。

## 源盘点复算
源材料已复制为设计参考，独立测试不要求原始压缩包。要重新核对原始8个压缩包/10个上位文档，可运行：

```sh
python tools/build_inventory.py --source-root /path/to/game_design --output /path/to/audit_output
```

不要把此命令输出覆盖已发布的正式资产。源盘点覆盖指定材料集合而非所有文件里的任意文本。

## 真实段落投影示例
```sh
python tools/preview_q102_request.py
```
只生成受限请求和手工说明fixture，无模型调用。

## 内容边界
没有真实音轨、没有新模型/供应商接入、没有实际人群校准；语义金标准等待真实模型与人工评估。data/Q102_Production_Bundle_v0.1.json应当无法通过player_release，缺项必须保留。

包内源正文沿用项目已有材料并标注出处；不附字体、第三方试卷或商业词库。文件哈希不是发布签名，真实密钥与身份在第十二部分实现。
