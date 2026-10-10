"""Refresh four engineering candidates from current real evidence, never human approval.

Default: read-only preflight, then save its report. --write-reports alone invokes
task_report.finish(partial=True). No tests, CI scripts, native process or ledger
update runs during preflight. Inputs and every inspected artifact are rechecked
before the existing task-report API is invoked.

python reports/continuous/finish_current_runtime_candidates.py \
  --suite-dir reports/build/FINAL --performance-dir reports/execution/CX-075/PERF \
  --performance-capture reports/continuous/PRE_NATIVE_CAPTURE.json \
  --native reports/build/CURRENT/chapter_two_client_results.json \
  --native reports/build/CURRENT/chapter_five_client_results.json

The optional external performance capture is created BEFORE its native process:
{captured_before_native_start:true,captured_utc:UTC_ISO8601,inputs:[path,sha256,bytes],
 binaries:[path,sha256],performance_command:{argv:[...],cwd:absolute_workspace},
 hardware?:{os,cpu,gpu,memory_mib,storage}}. A capture cannot renew an older run.
"""
from __future__ import annotations
import argparse
from contextlib import closing
from datetime import datetime,timezone
import hashlib
import json
import math
from pathlib import Path
import shutil
import sqlite3
import sys
import wave

sys.dont_write_bytecode=True
import finish_chapter_candidates as E
ROOT=E.ROOT
TASKS=('CX-066','CX-070','CX-071','CX-075')
EXPECTED={
 'CX-066':['只脚本不算音轨','关键否定/数值逐项核对','既有原文不被导入润色','无字体二进制来自交接包'],
 'CX-070':['拒绝不反复弹','剧情未知不入题','同模板换名不算迁移','不通过经验诱导无限刷题','离线天数不扣属性'],
 'CX-071':['短留言没有完整评分','新内容/音轨/支持变化使相关记录复核','训练库与未见测试隔离','人工签核不能由模型自报'],
 'CX-075':['不把容器脚本耗时当游戏FPS','长会话不漏NPC占用','主线程不卡持久写','不为性能删除合法草稿']}

def sources(*bases):
    result=set()
    for base in bases:
        path=E.inside(base)
        if path.is_file():result.add(E.rel(path));continue
        E.require(path.is_dir(),'Required current source directory missing: '+base)
        result.update(E.rel(p) for p in path.rglob('*') if p.is_file() and p.suffix in {'.cs','.py','.md','.json','.tsv'}
                      and not {'bin','obj','.godot','__pycache__'}.intersection(p.relative_to(ROOT).parts))
    return sorted(result)

def actual_case(actual,task,name):
    found=actual.get((task,name));E.require(found is not None,'Required exact current case missing: '+task+' / '+name)
    return {k:v for k,v in found.items() if k!='case_dir'}

def lexicon_and_audio(now):
    source=ROOT/'content/source/lexicon/learner_lexicon.json';installed=ROOT/'game/content/lexicon/learner_lexicon.json'
    lex=E.read(source);provenance=E.read('content/source/lexicon/provenance.author_only.json');entries=lex['entries']
    E.require(len(entries)==720 and len({e['id'] for e in entries})==720,'Current learner lexicon must contain 720 unique senses')
    counts={tier:sum(e['tier']==tier for e in entries) for tier in ['core','extended']}
    E.require(counts=={'core':360,'extended':360},'Current core/extended sense counts differ')
    E.require(all(e['term'].strip() and e['english_definition'].strip() and e['chinese_aid'].strip() and e['sense_key'].strip()
                  and e['contexts'] for e in entries),'Empty dictionary placeholders cannot count as senses')
    E.require(E.sha(source)==E.sha(installed)==provenance['package_sha256'],'Installed/source/provenance lexicon fingerprints differ')
    source_files=provenance['source_files'];E.require(source_files,'Lexicon lacks actual source provenance')
    source_registry={r['path']:r['sha256'] for r in source_files};E.require(len(source_registry)==len(source_files),'Duplicate lexicon original source record')
    for record in source_files:E.require(E.sha(record['path'])==record['sha256'],'Lexicon original source changed: '+record['path'])
    contexts=[c for e in entries for c in e['contexts']]
    for c in contexts:
        E.require(c['source_file_sha256']==source_registry.get(c['source_file']),'Lexicon context source fingerprint changed or is unregistered')
        E.require(c['paragraph_english'][c['start_scalar']:c['end_scalar']]==c['matched_form'],'Lexicon Unicode scalar excerpt differs')
    fonts=[E.info(p) for base in ['reference','content/source','game/content'] for p in (ROOT/base).rglob('*')
           if p.is_file() and p.suffix.lower() in {'.ttf','.otf','.woff','.woff2'}]
    binding_path=ROOT/'game/content/audio/regional_generation.json';bindings=E.read(binding_path);audios=[]
    E.require(bindings and len({b['material_id'] for b in bindings})==len(bindings),'Actual regional audio bindings missing or duplicated')
    for b in bindings:
        path=E.inside('game/content/'+b['audio_path'],ROOT/'game/content');facts=E.info(path)
        E.require(facts['sha256']==b['audio_sha256'] and facts['bytes']==b['bytes'],'Actual rendered WAV differs from its binding')
        with wave.open(str(path),'rb') as audio:
            frames,rate=audio.getnframes(),audio.getframerate()
            E.require(frames>0 and rate>0 and audio.getnchannels()>0 and audio.getsampwidth()>0,'Audio script/empty wave cannot count as a recording')
        E.require(b['review_status']=='rendered_candidate_not_auditioned' and b['redistribution_rights'].startswith('pending'),
                  'This candidate CLI cannot infer human audition/voice rights approval')
        audios.append({'material':b['material_id'],'version':b['material_version'],'actual_wave':facts,'pcm_frames':frames,'sample_rate':rate,
                       'duration_seconds':frames/rate,'review_status':b['review_status'],'rights':b['redistribution_rights']})
    return {'learner_source':E.info(source),'installed_learner':E.info(installed),'provenance':E.info('content/source/lexicon/provenance.author_only.json'),
      'content_version':lex['content_version'],'sense_counts':counts,'actual_context_count':len(contexts),'actual_source_documents':[E.info(r['path']) for r in source_files],
      'human_sense_review':lex['human_review'],'empirical_calibration':lex['empirical_calibration'],
      'sense_pronunciation_count':sum(bool(e['pronunciation_asset']) for e in entries),'actual_regional_audio_bindings':E.info(binding_path),
      'actual_rendered_recordings':audios,'font_binary_scan':{'roots':['reference','content/source','game/content'],'found':fonts},
      'five_channel_ui_source':E.info('game/UI/Audio/MistAudioChannels.cs'),
      'scope':'Actual authored definitions and source contexts, compiled offline package copy and rendered WAV bytes; no automatic semantic, pronunciation or human listening approval.'}

def performance_database(directory):
    profile=directory/'isolated_profile';control=profile/'control.sqlite'
    with closing(sqlite3.connect(E.readonly_uri(control),uri=True)) as db:pointers=db.execute('SELECT current_path FROM recovery_pointers').fetchall()
    E.require(len(pointers)==1,'Performance profile must have one real current world pointer')
    active=E.inside(profile/pointers[0][0],profile)
    with closing(sqlite3.connect(E.readonly_uri(active),uri=True)) as db:
        E.require(db.execute('PRAGMA integrity_check').fetchone()[0]=='ok','Actual performance database integrity failed')
        branch=db.execute("SELECT value FROM metadata WHERE key='active_branch'").fetchone()[0]
        receipts=db.execute('SELECT semantic_key,action_type,result_json FROM receipts WHERE branch_id=?',(branch,)).fetchall()
        E.require(receipts and all(not E.fixture_receipt(k,a) for k,a,_ in receipts),'Benchmark draft fixtures cannot seed adventure receipts')
        creation=[row for row in receipts if row[1]=='normal_campaign_created' and row[0].startswith('campaign:new:')]
        E.require(len(creation)==1 and type(json.loads(creation[0][2]).get('experience_awarded')) is int
                  and json.loads(creation[0][2])['experience_awarded']==0,'Native benchmark needs actual zero-XP campaign creation')
        xp=db.execute("SELECT quantity FROM balances WHERE branch_id=? AND account='experience'",(branch,)).fetchone()
        E.require(xp is None or xp[0]==0,'Native benchmark cannot retain adventure XP')
        heads=db.execute("SELECT h.task_id,h.draft_id,h.revision,r.body,r.body_hash,r.deleted FROM draft_heads h JOIN draft_revisions r ON r.draft_id=h.draft_id AND r.revision=h.revision WHERE h.branch_id=? AND h.task_id LIKE 'performance-fixture:%' ORDER BY h.task_id",(branch,)).fetchall()
        E.require(len(heads)==10 and {r[0] for r in heads}=={'performance-fixture:'+str(i) for i in range(10)},'Actual benchmark must retain all ten declared fixture heads')
        proofs=[];base=' '.join(['neutral benchmark word']*3000)
        for task,draft,revision,body,body_hash,deleted in heads:
            E.require(revision==2 and deleted==0 and body==base+' revision 1' and body_hash==hashlib.sha256(body.encode()).hexdigest(),'Current native benchmark head/body differs')
            prior=db.execute('SELECT revision,body,body_hash,deleted FROM draft_revisions WHERE draft_id=? ORDER BY revision',(draft,)).fetchall()
            E.require(len(prior)==2 and all(row[0]==i+1 and row[1]==base+' revision '+str(i) and row[2]==hashlib.sha256(row[1].encode()).hexdigest()
                         and row[3]==0 for i,row in enumerate(prior)),'Performance optimization erased or altered an acknowledged fixture draft revision')
            proofs.append({'task':task,'draft':draft,'retained_revisions':2,'current_body_sha256':body_hash})
    return {'control':E.info(control),'actual_current_database':E.info(active),'current_branch':branch,'experience':0,'drafts':proofs,
            'scope':'Twenty real neutral benchmark draft revisions retained; these are explicit engineering drafts, not world task or participant writing results.'}

def performance(directory,capture,now,suite_dir):
    path=directory/'native_performance.json';data=E.read(path);commands=E.commands(directory)
    E.require(not (directory/'native_performance_failure.json').exists(),'Native performance run recorded an actual failure')
    runs=[c for c in commands if c['argv'][-2:]==['--','--performance-candidate']]
    E.require(len(runs)==1,'Performance evidence must contain one exact native process command')
    command=runs[0];argv=command['argv']
    E.require(command['exit_code']==0 and command.get('cwd')==str(ROOT) and '--headless' not in argv and '--path' in argv
              and argv[argv.index('--path')+1]==str(ROOT/'game'),'Native performance must be a real nonheadless Godot command on this workspace')
    installed=E.read('toolchain/installed.windows-x64.json')
    E.require(Path(argv[0]).resolve()==Path(installed['godot']).resolve() and Path(argv[0]).is_file(),'Native performance command does not use the installed real Godot executable')
    log=E.inside(command['output_path']);lines=log.read_text(encoding='utf-8-sig');header=json.loads(lines.splitlines()[0])
    E.require(header['argv']==argv and header['exit_code']==0 and 'MISTSEA_NATIVE_PERFORMANCE frames=' in lines
              and 'ERROR:' not in lines.split('[stderr]',1)[-1],'Actual native process log does not verify the measurement')
    binaries=E.read(suite_dir/'compiled_artifacts.json')['binaries'];hardware={}
    if capture:
        record=E.read(capture);E.require(record.get('captured_before_native_start') is True,'External performance input capture must precede the native process')
        stamp=datetime.fromisoformat(record['captured_utc'].replace('Z','+00:00'));E.require(stamp.utcoffset() is not None,'Performance capture UTC offset missing')
        E.require(stamp.timestamp()<=min(path.stat().st_mtime,log.stat().st_mtime) and E.inside(capture).stat().st_mtime<=min(path.stat().st_mtime,log.stat().st_mtime),
                  'A newly made manifest cannot renew a historical performance run')
        E.require(record['performance_command']=={'argv':argv,'cwd':str(ROOT)},'Before-native capture belongs to another actual command')
        E.require(E.records_map(record['inputs'])==now and record['binaries']==binaries,'Before-native performance inputs/DLLs differ from current final suite')
        capture_proof=E.info(capture);hardware=record.get('hardware',{})
    else:
        inputs=directory/'run_inputs.json';compiled=directory/'compiled_artifacts.json'
        E.require(inputs.stat().st_mtime<=path.stat().st_mtime and compiled.stat().st_mtime<=path.stat().st_mtime,'Performance capture was made after measurement')
        E.require(E.records_map(E.read(inputs)['inputs'])==now,'Actual performance input capture differs from current complete inputs')
        E.validate_compiled_artifacts(directory);capture_proof={'inputs':E.info(inputs),'binaries':E.info(compiled)}
    E.require(data.get('human_signoff') is None and data['renderer']!='headless' and data['platform']=='Windows' and data['processor_count']>0
              and data['frames']>=60 and data['experience']==0 and data['retained_fixture_heads']==10,'Native benchmark metadata/scope differs')
    for key in ['mean_frame_interval_ms','p95_frame_interval_ms','p99_frame_interval_ms','observed_frame_cadence_fps','working_set_bytes','managed_heap_bytes','sqlite_bytes']:
        E.require(isinstance(data[key],(int,float)) and math.isfinite(data[key]) and data[key]>0,'Invalid actual performance measurement: '+key)
    E.require(data['p99_frame_interval_ms']>=data['p95_frame_interval_ms'] and abs(data['observed_frame_cadence_fps']-1000/data['mean_frame_interval_ms'])<1e-6
              and data['frames']*data['mean_frame_interval_ms']>=7500,'Actual frame cadence is inconsistent or shorter than the declared native observation')
    writes=data['writes'];E.require(len(writes)==20 and {(w['task'],w['revision']) for w in writes}=={(task,rev) for task in range(10) for rev in [1,2]},'Twenty acknowledged native fixture revisions required')
    E.require(all(w['enqueue_ms']>=0 and w['total_ack_ms']>=w['enqueue_ms'] and w['body_chars']>60000 for w in writes),'Native write timing/body facts inconsistent')
    missing=[k for k in ['os','cpu','gpu','memory_mib','storage'] if not hardware.get(k)]
    return {'actual_measurement':E.info(path),'data':data,'actual_commands':E.info(directory/'commands.json'),'actual_native_log':E.info(log),
      'pre_native_capture':capture_proof,'installed_toolchain':E.info('toolchain/installed.windows-x64.json'),
      'hardware_details':hardware,'hardware_labels_missing':missing,'actual_draft_retention':performance_database(directory),
      'scope':'Current Debug first scene/9000-word editor and 20 declared fixture draft revisions; real process frame cadence, no 78-scene, long NPC session, release-build or clean-machine claim.'}

def criterion(text,status,cases,note,extra=None):
    return {'criterion':text,'status':status,'evidence':{'actual_current_named_cases':cases,**(extra or {})},'note':note}

def preflight(args):
    suite_dir=E.inside(args.suite_dir);compiled_dir=E.inside(args.compiled_build_dir or args.suite_dir)
    current=E.current_inputs();now=E.records_map(current);compilation=E.validate_compilation(compiled_dir,now)
    E.require(not (suite_dir/'reused_compilation.json').exists(),'Runtime candidates require a fresh final full suite')
    suite,actual,production=E.validate_full_suite(suite_dir)
    suite_inputs=suite_dir/'run_inputs.json';E.require(E.records_map(E.read(suite_inputs)['inputs'])==now,'Final suite run inputs differ from current complete inputs')
    E.require(E.records_map(E.read(suite_dir/'build_inputs.json')['inputs'])==now,'Fresh full-suite build inputs differ from current complete inputs')
    context={'directory':suite_dir,'actual':actual,'inputs':E.info(suite_inputs)}
    natives=[E.validate_native(E.inside(p),now,{},context) for p in args.native]
    E.require(len({n['result']['path'] for n in natives})==len(natives),'Duplicate native result input')
    assets=lexicon_and_audio(now);perf=performance(E.inside(args.performance_dir),args.performance_capture,now,suite_dir)
    plan=E.read('planning/plan.json');tasks={t['id']:t for t in plan['tasks']};state=E.read('planning/state/execution.json')
    checks=[c['name'] for n in natives for c in n['checks'] if c['passed'] is True]
    features={'lexicon_native_720_entries_and_distinct_senses','lexicon_native_actual_permitted_context_is_shown','lexicon_native_future_private_contexts_remain_absent',
              'five_audio_channels_and_content_route','audio_independent_volume_saved_without_world_reward','audio_preferences_reload_actual_bus'}
    support={'verified_current_native_features':sorted(features.intersection(checks)),'current_native_features_not_executed_in_supplied_reports':sorted(features-set(checks))}
    cases=lambda task:[{k:v for k,v in item.items() if k!='case_dir'} for (t,_),item in actual.items() if t==task]
    exact=lambda task,name:actual_case(actual,task,name)
    selected={t:cases(t) for t in TASKS};E.require(all(selected.values()),'Current full suite lacks one of the four actual runtime task case sets')
    for t in TASKS:E.require(tasks[t]['kind']=='engineering' and tasks[t]['acceptance']==EXPECTED[t],'Immutable runtime task criteria changed: '+t)
    acceptance={
     'CX-066':[
       criterion(EXPECTED['CX-066'][0],'passed',[exact('CX-066','Script-only audio has null hash/bytes/duration; JSON audition claims and invented timecodes cannot certify a recording'),exact('CX-066','missing_audio_selects_script_reading_and_cannot_be_listening_evidence')],'Actual parser/routing refusals plus measured current rendered WAV bytes; scripted reading never becomes an audio/listening claim.',{'actual_assets':assets}),
       criterion(EXPECTED['CX-066'][1],'pending',[exact('CX-066','Critical listening checklist retains exact negation/quantity anchors and stays pending human full audition')],'Exact source ranges and critical anchors are checked automatically. CAI baseline requires a human to hear the final mix; no audition is self-signed.'),
       criterion(EXPECTED['CX-066'][2],'passed',[exact('CX-066','Frozen six-turn Q102 original stays exact; raw speaker labels are detected and reviewed regeneration gets explicit source ranges'),exact('CX-066','lexicon_all_contexts_match_exact_actual_learner_paragraphs_versions_hashes_and_unicode_coordinates')],'Actual frozen-original fidelity and all current source-bound contexts pass; speech projection is separately ranged, never an overwrite of the original.'),
       criterion(EXPECTED['CX-066'][3],'passed' if not assets['font_binary_scan']['found'] else 'pending',[],'Actual reference/source/player-input font scan is recorded. No handover font binary is added to these candidate inputs.',{'actual_font_scan':assets['font_binary_scan']})],
     'CX-070':[
       criterion(text,'passed',[exact('CX-070',name)],'Executed engineering policy/SQLite projection guard; synthetic cases are explicitly policy fixtures and do not supply a human-calibrated practice library.')
       for text,name in zip(EXPECTED['CX-070'],[
        'Refusal survives the next safe opportunity and uncertain clock; only a deliberate new request overrides it',
        'Known facts, current access, release, 6.5 target and training partition filter before any title is returned',
        'Canonical families defeat renaming and hashes; delayed transfer requires three families and verified real elapsed time',
        'Unbound real review sources yield an honest empty advisory report with no reward or adventure penalty',
        'Clock anomalies leave outcome history intact and timing unverified, without offline-day decay'])],
     'CX-071':[
       criterion(text,'passed',[exact(task,name)],'Actual candidate rule/authority refusal verified in the final suite; no full human score, calibrated 6.5 material or pilot signature is inferred.')
       for text,(task,name) in zip(EXPECTED['CX-071'],[
        ('CX-071','Complete writing needs two distinct trained blind human raters; short text and simulated raters never produce a full band'),
        ('CX-071','Material, question, support, audio, order, tool and evaluator changes invalidate only exact bound calibration records'),
        ('CX-070','Automatic practice excludes exposed families; deliberate repetition remains practice and never a new transfer source'),
        ('CX-071','Default calibration inspector accepts no model, fixture or hash-integrity self-signature as a human decision')])],
     'CX-075':[
       criterion(EXPECTED['CX-075'][0],'passed',selected['CX-075'],'Actual nonheadless Windows Godot frame intervals and local operation timings, never shell-script duration as FPS.',{'actual_native_measurement':perf}),
       criterion(EXPECTED['CX-075'][1],'pending',[],'This eight-second first-scene fixture probe does not exercise a long native NPC occupancy session.'),
       criterion(EXPECTED['CX-075'][2],'pending',[],'Real enqueue/ack/frame observations are retained. Twenty writes do not establish all-screen, long-session main-thread latency guarantees.'),
       criterion(EXPECTED['CX-075'][3],'passed',[],'Actual readonly current SQLite retains all ten declared neutral heads and both exact acknowledged revisions. Scope is this native benchmark, not all future sessions.',{'actual_draft_retention':perf['actual_draft_retention']})]}
    all_source={
     'CX-066':sources('src/Mist.Core/Assets','src/Mist.Application/Assets','src/Mist.Core/Learning','src/Mist.Application/Learning','tests/Mist.Tests/Assets','tests/Mist.Tests/Learning',
                     'game/UI/Audio/MistAudioChannels.cs','game/UI/Shell/Root.RegionalLearning.cs','game/UI/Campaign/CampaignClient.RegionalLearning.cs','game/Diagnostics/Root.RegionalLearningClientTests.cs',
                     'src/Mist.Infrastructure/Storage/ProfileStore.RegionalLearning.cs','content/source/lexicon/learner_lexicon.json','content/source/lexicon/provenance.author_only.json',
                     'content/source/lexicon/senses.author_only.tsv','content/source/lexicon/history.author_only/LEX-20261010-v2.json',
                     'tools/audit/build_regional_lexicon.py','tools/audit/lexicon_source_adapters.py','game/content/audio/regional_generation.json'),
     'CX-070':sources('src/Mist.Core/ReviewCalibration','src/Mist.Application/ReviewCalibration','tests/Mist.Tests/ReviewCalibration','src/Mist.Core/Learning','src/Mist.Application/Learning','tests/Mist.Tests/Learning'),
     'CX-071':sources('src/Mist.Core/ReviewCalibration','src/Mist.Application/ReviewCalibration','tests/Mist.Tests/ReviewCalibration','tools/Mist.ReviewCalibration','tools/ci/review_audit.py'),
     'CX-075':sources('game/Diagnostics/Root.PerformanceCandidate.cs','game/Diagnostics/CampaignClient.PerformanceCandidate.cs','src/Mist.Core/ReleaseReadiness/PerformanceEvidenceRules.cs',
                     'src/Mist.Infrastructure/Storage/ProfileStore.Drafts.cs','src/Mist.Infrastructure/Storage/SingleWriter.cs','tools/ci/native_performance.py',
                     'tests/Mist.Tests/ReleaseReadiness/ReleaseReadinessCases.cs','game/Root.cs','game/MistSea.csproj')}
    prepared=[];skipped=[]
    for t in TASKS:
        old=ROOT/tasks[t]['evidence_path']
        if state['tasks'][t]['status']=='accepted' or (old.is_file() and E.read(old).get('result')=='accepted'):skipped.append(t);continue
        prepared.append({'task_id':t,'evidence_path':tasks[t]['evidence_path'],'acceptance':acceptance[t],'sources':[E.info(p) for p in all_source[t]],
                         'executed_current_cases':selected[t],'result':'implemented','human_signoff':None})
    return {'schema_version':1,'generated_utc':datetime.now(timezone.utc).isoformat(),'scope':'Current runtime engineering candidates only, no whole-GDD or human acceptance',
      'suite':E.info(suite_dir/'test_results.json'),'suite_registry':E.info(suite_dir/'full_suite_inventory.json'),'suite_inputs':E.info(suite_inputs),'suite_build_inputs':E.info(suite_dir/'build_inputs.json'),
      'plan':E.info('planning/plan.json'),'report_cli':E.info(Path(__file__)),'evidence_validation_module':E.info(Path(E.__file__)),
      'compilation':compilation,'current_inputs':current,'required_actual_normal_production':production,
      'assets_and_lexicon':assets,'native_features':support,'actual_current_native':natives,'actual_performance':perf,'prepared_tasks':prepared,'accepted_history_skipped':skipped,
      'fulfilled_production_candidates':['720 distinct CORE/EXTENDED senses with actual English definitions, Chinese aids and source contexts; compiled offline copy',
        'Actual regional WAV candidates, exact source/version/bytes bindings and current automated audio/lexicon rules','Five-channel native volume UI exists; supplied current native check coverage listed separately',
        'Real local due-review policy, response/delay/privacy history and source-scope guards; current final named cases record the tested boundaries'],
      'not_run':['Human final mixed speech/critical negation-number audition, pronunciation recordings/review and redistribution rights',
        'Actual target-band participant pilots, real human blind raters, canon/semantic calibration and unfamiliar full language assessment',
        'Physical IME/screen reader, clean second machine, release-build performance, physical power loss and full 78-scene long NPC occupancy load',
        'Missing detailed hardware labels: '+', '.join(perf['hardware_labels_missing']) if perf['hardware_labels_missing'] else 'Hardware labels supplied; no independent physical-machine certification'],
      'execution_json_written_by_this_script':False,'ledger_write_authority':'Only task_report.finish, when explicitly invoked with --write-reports'}

def write_reports(args,audit):
    E.require(E.records_map(audit['current_inputs'])=={r['path']:r['sha256'] for r in E.current_inputs()},'Current inputs changed after runtime preflight')
    E.recheck_artifact_records(audit)
    suite_dir=E.inside(args.suite_dir);E.validate_compiled_artifacts(suite_dir)
    E.validate_compilation(E.inside(args.compiled_build_dir or args.suite_dir),{r['path']:r['sha256'] for r in audit['current_inputs']})
    _,actual,_=E.validate_full_suite(suite_dir)
    context={'directory':suite_dir,'actual':actual,'inputs':audit['suite_inputs']}
    for native in audit['actual_current_native']:
        E.validate_native(E.inside(native['result']['path']),{r['path']:r['sha256'] for r in audit['current_inputs']},{},context)
    digest=hashlib.sha256(json.dumps({k:v for k,v in audit.items() if k!='generated_utc'},sort_keys=True).encode()).hexdigest()[:12]
    stage=ROOT/'reports/continuous/rt'/digest;stage.mkdir(parents=True,exist_ok=True)
    frozen=stage/'audit.json';frozen.write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    required={r['path'] for task in audit['prepared_tasks'] for r in task['sources']}|{E.rel(Path(__file__)),audit['plan']['path'],audit['suite']['path'],audit['suite_registry']['path'],audit['suite_inputs']['path']}
    pool={};origins=[]
    for index,origin in enumerate(sorted(required),1):
        source=E.inside(origin);dest=stage/(f'f{index:04d}'+(source.suffix if len(source.suffix)<=12 else '.dat'))
        E.require(len(str(ROOT/'reports/execution/CX-071/evidence/20261010T000000000000Z/sources'/dest.relative_to(ROOT)))<240,'Runtime task source alias exceeds conservative Windows snapshot limit')
        shutil.copyfile(source,dest);pool[origin]=E.rel(dest);origins.append({**E.info(source),'snapshot_alias':E.info(dest)})
    manifest=stage/'origins.json';manifest.write_text(json.dumps({'files':origins},ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    sys.path.insert(0,str(ROOT/'reports/continuous'));from task_report import finish
    outputs=[];commands=E.commands(E.inside(args.suite_dir))+E.commands(E.inside(args.performance_dir))
    for native in audit['actual_current_native']:commands+=E.commands(E.inside(native['result']['path']).parent)
    for task in audit['prepared_tasks']:
        state=E.read('planning/state/execution.json')
        if state['tasks'][task['task_id']]['status']=='accepted':continue
        existing=ROOT/task['evidence_path']
        if existing.is_file():
            old=E.read(existing);old_manifest=old.get('working_tree_manifest')
            if old_manifest and E.inside(old_manifest).is_file() and E.read(old_manifest).get('interface_schema_impact',{}).get('candidate_input_fingerprint')==digest:
                for artifact in old.get('artifacts',[]):E.require(E.sha(artifact['path'])==artifact['sha256'],'Identical prior runtime candidate artifact changed')
                outputs.append({'task':task['task_id'],'result':old['result'],'evidence':task['evidence_path'],'identical_candidate_preserved':True});continue
        sources=[pool[r['path']] for r in task['sources']]+[pool[E.rel(Path(__file__))],E.rel(frozen),E.rel(manifest)]
        report=finish(task['task_id'],sources,commands,task['acceptance'],audit['not_run'],
          {'scope':audit['scope'],'executed':len(task['executed_current_cases']),'failed':0,'actual_current_cases':task['executed_current_cases'],
           'suite':audit['suite'],'actual_native':audit['actual_current_native'],'actual_performance':audit['actual_performance'],'human_signoff':None},
          changes={'data_migration_needed':False,'candidate_input_fingerprint':digest,'short_source_origin_manifest':E.rel(manifest),'no_human_signature':True},partial=True)
        outputs.append({'task':task['task_id'],'result':report['result'],'evidence':task['evidence_path']})
    return outputs

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--suite-dir',required=True);parser.add_argument('--compiled-build-dir')
    parser.add_argument('--performance-dir',required=True);parser.add_argument('--performance-capture');parser.add_argument('--native',action='append',default=[])
    parser.add_argument('--output',default='reports/continuous/current_runtime_preflight.json');parser.add_argument('--write-reports',action='store_true');args=parser.parse_args()
    try:
        audit=preflight(args)
        if args.write_reports:audit['task_report_api_results']=write_reports(args,audit)
        out=E.inside(args.output,ROOT/'reports/continuous');out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
        print(json.dumps({'preflight':E.rel(out),'prepared':[t['task_id'] for t in audit['prepared_tasks']],'reports_written':args.write_reports,'human_signoff':None},ensure_ascii=False));return 0
    except (E.EvidenceError,ValueError,KeyError,OSError,sqlite3.DatabaseError,wave.Error) as error:
        print(json.dumps({'result':'blocked_before_runtime_report_update','error':str(error),'human_signoff':None},ensure_ascii=False));return 2

if __name__=='__main__':sys.exit(main())
