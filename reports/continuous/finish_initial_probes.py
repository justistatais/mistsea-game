import json
from pathlib import Path
from task_report import ROOT, finish


def load(path): return json.loads(path.read_text(encoding="utf8"))
def files(folder): return [p.relative_to(ROOT).as_posix() for p in (ROOT / folder).glob("*") if p.is_file()]


for ident, folder in [("CX-003", "spikes/ui"), ("CX-004", "spikes/storage"), ("CX-005", "spikes/exports/OfflineCandidate")]:
    task = next(t for t in load(ROOT / "planning/plan.json")["tasks"] if t["id"] == ident)
    evidence = ROOT / "reports/execution" / ident
    latest = load(evidence / "latest.json")
    assert latest["result"] == "verified"
    run = ROOT / latest["path"]
    commands = load(run / "commands.json")
    sources = files(folder) + [(run / "result.json").relative_to(ROOT).as_posix()]
    tests = {"automated_run": latest["path"], "scope": "native Windows Godot/.NET with automated checks"}
    if ident == "CX-003":
        output = load(run / "result.json")["runtime_output"]
        payload = json.loads(next(line.removeprefix("MISTSEA_UI_RESULTS ") for line in output.splitlines() if line.startswith("MISTSEA_UI_RESULTS ")))
        assert all(payload["checks"].values())
        tests.update(payload)
        sources.append("reports/execution/CX-003/native_window_observation.json")
        details = ["原生TextEdit合成WASD/回车和Windows物理W/回车不触发场景；聚焦按钮也有隔离回归。",
                   "100%/200%均测试Tab到末行按钮；真实窗口在200%下可用Tab、回车激活。",
                   "合成组合输入结果与真实IME的not_run状态分别记录。",
                   "可访问树及控件名称已记录，未声称真实读屏兼容；焦点字段延迟/不一致留待HV-001。"]
    else:
        exported = load(evidence / "latest_export.json")
        assert exported["result"] == "verified"
        export_run = ROOT / exported["path"]
        export_result = load(export_run / "export_result.json")
        commands += export_result["commands"]
        sources.append((export_run / "export_result.json").relative_to(ROOT).as_posix())
        tests.update(export=exported["path"], native_files=export_result["native_files"], bundled_runtime=export_result["runtime_bundled"])
        if ident == "CX-004":
            result = load(run / "storage_result.json")
            assert all(result["checks"].values())
            tests.update(result)
            details = ["两表提交、写入中断和约束失败均核对真实数据库。", "实际竞争写入返回database_busy，耗时与配置写入证据。",
                       "Windows导出副本移除e_sqlite3.dll后返回原生库加载诊断和退出码3。", "Godot .NET与导出exe均在当前Windows x64运行，原生版本3.53.3和部署哈希已记录。"]
        else:
            assert any(Path(c["output_path"]).stem == "reopen_saved_draft" and c["exit_code"] == 0 for c in commands)
            assert any(Path(c["output_path"]).stem == "corrupt_database_diagnostic" and c.get("expected_nonzero_proven") for c in commands)
            details = ["Windows导出含运行时和SQLite，移除SDK环境后保存3000词正文并在另一个进程重开。",
                       "代码只使用本地文件和SQLite；没有网络供应商或个人正文上传调用。", "损坏文件返回明确诊断/退出码4，校验原文件未被重建。",
                       "独立干净Windows仍为not_run，HV-001待人工核验，G0不开放。"]
    acceptance = [{"requirement": requirement, "status": "passed", "detail": detail, "evidence": latest["path"]}
                  for requirement, detail in zip(task["acceptance"], details)]
    finish(ident, sources, commands, acceptance,
           ["真实中文输入法逐步核验", "真实读屏软件及人工验证", "另一台无编辑器/SDK的干净Windows", "产品、语言校准和发布验收"], tests)

# Prepare the actual human gate without substituting a model signature.
human = ROOT / "reports/human/HV-001"
human.mkdir(parents=True, exist_ok=True)
(human / "待测步骤.md").write_text("# HV-001 待测\n\n使用CX-005最新Windows候选，记录设备、系统、输入法、读屏版本和构建文件哈希。\n\n1. 中文组合输入、候选提交、撤销与重做；正文WASD、Enter不操作场景。\n2. 3000词、中英文、组合重音及emoji；200%下Tab/回车到达末行按钮。\n3. 用实际读屏检查名字、正文、焦点和弹窗；Godot可访问树焦点字段已发现不一致，须检查。\n4. 在无编辑器/SDK的干净Windows离线启动，保存、关闭重开；损坏数据库不得静默重置。\n\n本文件是步骤，没有人工测试结果或签名。G0保持关闭。\n", encoding="utf8")
