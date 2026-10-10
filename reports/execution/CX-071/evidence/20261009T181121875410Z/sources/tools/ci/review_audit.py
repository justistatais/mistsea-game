"""Run the actual read-only review tool and preserve a draft protocol with no human authority."""
from datetime import datetime,timezone
import hashlib,json,os
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[2]
def hash_json(value):return hashlib.sha256(json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(",",":")).encode("utf8")).hexdigest()
def main():
    installed=json.loads((ROOT / "toolchain/installed.windows-x64.json").read_text(encoding="utf8"))
    out=ROOT / "reports/execution/CX-071" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ");out.mkdir(parents=True)
    env=os.environ.copy();env.update(DOTNET_ROOT=installed["dotnet_root"],DOTNET_ROOT_X64=installed["dotnet_root"])
    compiler=ROOT / "tools/Mist.ReviewCalibration/bin/Debug/net10.0/Mist.ReviewCalibration.dll"
    source=ROOT / "reference/part05/data/Original_Materials_v0.1.json"
    original=json.loads(source.read_text(encoding="utf8"));material=next(m for m in original["materials"] if m["id"]=="LAB-R-001")
    protocol={"binding":{"material":material["id"],"version":material["version"],"modality":"reading","task_kind":"long_reading",
        "text_hash":hashlib.sha256("\n\n".join(p["text"] for p in material["paragraphs"]).encode("utf8")).hexdigest(),
        "questions_hash":hash_json(material["items"]),"audio_hash":"not_applicable",
        "support_hash":hash_json({"scope":"draft comparison protocol","support":"future conditions must name every tool and exposure"}),
        "viewing_order_hash":hash_json({"order":"source then questions; candidate proposal only"}),
        "time_and_tools_hash":hash_json({"clock":"not enforced by this audit","tools":"draft declaration requiring reviewer confirmation"}),
        "skills_version":"source-part05-v0.1","evaluator_version":"disabled-no-calibrated-evaluator","rules_version":"mist-rules-1","target":6.5},
        "exam_reference":"IELTS Academic","exam_reference_version":"baseline-v0.1-official-reference-confirmation-pending","relevant_section":"reading",
        "participant_stratification":"Planned relevant reading section 6.0/6.5/7.0; no actual participants registered.",
        "exclusion_rules":"Draft: no unconsented, overall-only, incomplete or prior-family-exposed responses in a primary comparison.",
        "conditions_note":"Draft local planning only; two actual editors, real target-group pilot, monitoring and a calibration decision are missing."}
    protocol_path=out / "draft_protocol.json";protocol_path.write_text(json.dumps(protocol,ensure_ascii=False,indent=2),encoding="utf8")
    commands=[]
    def run(name,arg,path,expected=0,marker=None):
        argv=[installed["dotnet"],str(compiler),arg,str(path)];result=subprocess.run(argv,cwd=ROOT,env=env,capture_output=True,timeout=60)
        stdout=result.stdout.decode("utf8",errors="replace");stderr=result.stderr.decode("utf8",errors="replace")
        log=out / (name+".log");log.write_text(json.dumps({"argv":argv,"exit_code":result.returncode})+"\n[stdout]\n"+stdout+"\n[stderr]\n"+stderr,encoding="utf8")
        valid=result.returncode==expected and (marker is None or marker in stdout+stderr)
        command={"argv":argv,"cwd":str(ROOT),"exit_code":result.returncode,"output_path":log.relative_to(ROOT).as_posix()}
        if expected:command.update(expected_nonzero_proven=valid,expected_failure_reason=marker)
        commands.append(command);(out / "commands.json").write_text(json.dumps(commands,ensure_ascii=False,indent=2),encoding="utf8")
        assert valid,log
        return json.loads(stdout) if expected in {0,3} else None
    inventory=run("source_inventory","audit-source",source)
    refusal=run("unapproved_protocol_refusal","audit-protocol",protocol_path,3,"draft_target_65")
    injection=out / "invalid_approval_field.json";injection.write_text(json.dumps(protocol|{"approved":True},ensure_ascii=False,indent=2),encoding="utf8")
    run("unknown_approval_field_refusal","audit-protocol",injection,1,"LOCAL_REVIEW_AUDIT_REJECTED")
    result={"result":"read_only_candidate_audit_verified","source_sha256":hashlib.sha256(source.read_bytes()).hexdigest(),"inventory":inventory,"draft_inspection":refusal,"human_signoff":None,"actual_participants":0,"commands":commands}
    (out / "audit_result.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf8")
    print(json.dumps({"result":result["result"],"path":out.relative_to(ROOT).as_posix(),"actual_participants":0},ensure_ascii=False))
if __name__=="__main__":main()
