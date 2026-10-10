"""Execute the local renderer and measure actual WAVE files; no audition or alignment claim."""
from datetime import datetime,timezone
import hashlib,json,subprocess,sys,wave
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]

def main():
    stamp=datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    out=ROOT/"reports/execution/CX-066"/stamp;out.mkdir(parents=True)
    commands=[]
    jobs=[("prepare",[sys.executable,str(ROOT/"tools/audio/prepare_regional.py")]),
          ("render_or_verify",[r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe","-NoProfile","-ExecutionPolicy","Bypass","-File",str(ROOT/"tools/audio/Generate-RegionalSpeech.ps1"),"-InputPath",str(ROOT/"content/source/audio/regional_speech_input.json"),"-ContentDirectory",str(ROOT/"game/content")])]
    for name,argv in jobs:
        result=subprocess.run(argv,cwd=ROOT,capture_output=True,timeout=120)
        log=out/(name+".log");log.write_text(result.stdout.decode("utf8",errors="replace")+"\n"+result.stderr.decode("utf8",errors="replace"),encoding="utf8")
        commands.append({"argv":argv,"cwd":str(ROOT),"exit_code":result.returncode,"output_path":log.relative_to(ROOT).as_posix()})
        (out/"commands.json").write_text(json.dumps(commands,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
        if result.returncode:raise RuntimeError(f"Actual local candidate renderer failed: {log}")
    tracks=json.loads((ROOT/"game/content/audio/regional_generation.json").read_text(encoding="utf8"))
    actual=[]
    for track in tracks:
        path=ROOT/"game/content"/track["audio_path"];body=path.read_bytes()
        assert hashlib.sha256(body).hexdigest()==track["audio_sha256"] and len(body)==track["bytes"]
        with wave.open(str(path),"rb") as audio:
            facts={"sample_rate":audio.getframerate(),"channels":audio.getnchannels(),"bits_per_sample":audio.getsampwidth()*8,"frames":audio.getnframes(),"duration_seconds":audio.getnframes()/audio.getframerate()}
        assert facts["duration_seconds"]>0
        actual.append({"material_id":track["material_id"],"material_version":track["material_version"],"audio_path":track["audio_path"],"audio_sha256":track["audio_sha256"],"bytes":len(body),"text_sha256":track["text_sha256"],"speech_sha256":track["speech_sha256"],"measured":facts})
    report={"scope":"actual local SAPI candidate rendering/reuse and exact WAVE byte/frame inspection", "tracks":actual,"count":len(actual),"voice":"Microsoft Zira Desktop - English (United States)","human_audition":None,"subtitle_timecodes":None,"target_calibration":"pending","voice_redistribution_rights":"pending_HV-004","distinct_dialogue_voices":"pending"}
    (out/"regional_audio_measurements.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
    print(json.dumps({"path":str(out.relative_to(ROOT)),"actual_tracks":len(actual),"audition":"pending"}))

if __name__=="__main__":main()
