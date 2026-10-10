"""Generate two offline SAPI candidates in an isolated report, without changing a shared index."""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import re

ROOT=Path(__file__).resolve().parents[2]
VOICE='Microsoft Zira Desktop - English (United States)'
ROLES=['Repairer:','Innkeeper:','Traveller:','Stage organiser:','Second performer:']
ROLE_PATTERN=re.compile(r'(?<!\S)('+ '|'.join(re.escape(r)for r in ROLES)+')')
def sha(value):return hashlib.sha256(value).hexdigest()
def turns(paragraph):
    original=paragraph['en'];matches=list(ROLE_PATTERN.finditer(original));assert matches and matches[0].start()==0
    segments=[]
    for i,match in enumerate(matches):
        prefix=match.group();start=match.end()
        while start<len(original)and original[start].isspace():start+=1
        end=matches[i+1].start()if i+1<len(matches)else len(original)
        spoken=original[start:end];assert spoken.strip()and not ROLE_PATTERN.search(spoken)
        segments.append(dict(paragraph_id=paragraph['id'],literal_prefix=prefix,speaker_id=prefix[:-1],source_start_scalar=start,source_end_scalar=end,text=spoken,sha256=sha(spoken.encode())))
    # Every original scalar belongs to an exact role label, following whitespace or a spoken slice.
    cursor=0
    for segment in segments:
        prefix=segment['literal_prefix'];assert original[cursor:cursor+len(prefix)]==prefix;cursor+=len(prefix)
        assert original[cursor:segment['source_start_scalar']].isspace();assert original[segment['source_start_scalar']:segment['source_end_scalar']]==segment['text'];cursor=segment['source_end_scalar']
    assert cursor==len(original)
    return segments
def main():
    stamp=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    report=ROOT/'reports/execution/CX-066'/('r1-life-'+stamp);content=report/'content';content.mkdir(parents=True)
    source=ROOT/'content/source/r1_life/learner_package.json';package=json.loads(source.read_text(encoding='utf8'));tracks=[]
    for m in package['materials']:
        if m['kind']!='listening_script':continue
        segments=[s for p in m['paragraphs']for s in turns(p)]
        original='\n\n'.join(p['en']for p in m['paragraphs']);speech='\n\n'.join(s['text']for s in segments)
        tracks.append(dict(material_id=m['id'],material_version=package['content_version'],text_sha256=sha(original.encode()),speech_sha256=sha(speech.encode()),audio_version='local-tts-1',voice=VOICE,audio_path=f"audio/regional/{m['id']}-{package['content_version']}-local-tts-1.wav",segments=segments,speech_text=speech,review_status='rendered_candidate_not_auditioned',redistribution_rights='pending_HV-004'))
    assert len(tracks)==2 and all(t['speech_text'] for t in tracks)
    prepared=report/'r1_life_speech_input.json';prepared.write_text(json.dumps(tracks,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    argv=[r'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'tools/audio/Generate-RegionalSpeech.ps1'),'-InputPath',str(prepared),'-ContentDirectory',str(content)]
    result=subprocess.run(argv,cwd=ROOT,capture_output=True,timeout=120)
    (report/'render.log').write_text(result.stdout.decode('utf8',errors='replace')+'\n'+result.stderr.decode('utf8',errors='replace'),encoding='utf8')
    (report/'commands.json').write_text(json.dumps(dict(argv=argv,cwd=str(ROOT),exit_code=result.returncode),ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    if result.returncode:raise RuntimeError(f'Actual local renderer failed; see {report}/render.log')
    generated=json.loads((content/'audio/regional_generation.json').read_text(encoding='utf8'));measured=[]
    for t in generated:
        path=content/t['audio_path'];raw=path.read_bytes();assert sha(raw)==t['audio_sha256']and len(raw)==t['bytes']
        with wave.open(str(path),'rb')as audio:
            pcm=dict(sample_rate=audio.getframerate(),channels=audio.getnchannels(),bits_per_sample=audio.getsampwidth()*8,frames=audio.getnframes(),duration_seconds=audio.getnframes()/audio.getframerate())
        assert pcm['frames']>0
        measured.append(dict(material_id=t['material_id'],source=source.relative_to(ROOT).as_posix(),source_file_sha256=sha(source.read_bytes()),material_version=t['material_version'],text_sha256=t['text_sha256'],speech_sha256=t['speech_sha256'],audio_sha256=t['audio_sha256'],bytes=t['bytes'],actual_file=path.relative_to(ROOT).as_posix(),target_game_content=t['audio_path'],measured=pcm))
    (report/'r1_life_audio_measurements.json').write_text(json.dumps(dict(scope='actual local SAPI render and WAVE byte/frame inspection; isolated content folder; shared index untouched',tracks=measured,count=len(measured),human_audition=None,subtitle_timecodes=None,alignment='exact source slices only; no temporal alignment',distinct_dialogue_voices='not_produced',language_calibration='human_pending',redistribution_rights='pending_HV-004'),ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    print(json.dumps(dict(report=report.relative_to(ROOT).as_posix(),tracks=len(measured),manifest=(content/'audio/regional_generation.json').relative_to(ROOT).as_posix(),human_audition='pending'),ensure_ascii=False))
if __name__=='__main__':main()
