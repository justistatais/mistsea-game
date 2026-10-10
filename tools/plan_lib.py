"""Read-only plan validation. This does not execute or certify game implementation."""
from __future__ import annotations
import hashlib
import json
from pathlib import Path, PurePosixPath
from typing import Any

ALLOWED_STATUS = {'not_started','in_progress','implemented','verified','accepted','blocked','deferred'}

def _unique_pairs(pairs):
    out = {}
    for key, value in pairs:
        if key in out:
            raise ValueError(f'duplicate JSON key: {key}')
        out[key] = value
    return out

def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=_unique_pairs)

def safe_relative(value: str) -> bool:
    p = PurePosixPath(value)
    return bool(value) and not p.is_absolute() and '..' not in p.parts and '\\' not in value and ':' not in value and '\x00' not in value

def all_dependencies(node: dict, features: dict) -> list[str]:
    result = list(node.get('depends_on', []))
    for feature, deps in node.get('optional_when_enabled', {}).items():
        if features.get(feature, False):
            result.extend(deps)
    return list(dict.fromkeys(result))

def validate_plan(plan: dict, trace: dict | None = None) -> list[str]:
    errors: list[str] = []
    target = plan.get('language_target', {})
    if target != {'reading':6.5,'listening':6.5,'writing':6.5,'speaking':None,'auto_raise':False}:
        errors.append('LANGUAGE_TARGET: must be separate R/L/W 6.5; speaking null; auto_raise false')
    nodes = plan.get('tasks', []) + plan.get('gates', [])
    ids = [n.get('id') for n in nodes]
    if len(ids) != len(set(ids)):
        errors.append('DUPLICATE_ID')
    by_id = {n.get('id'): n for n in nodes}
    # Validate both optional and default dependency graphs.
    for features in [plan.get('features', {}), {'online_model':True}]:
        graph = {n['id']: all_dependencies(n, features) for n in nodes}
        for key, deps in graph.items():
            for dep in deps:
                if dep not in graph:
                    errors.append(f'UNKNOWN_DEPENDENCY: {key} -> {dep}')
        visited: set[str] = set(); active: set[str] = set()
        def visit(key: str):
            if key not in graph or key in visited: return
            if key in active:
                errors.append(f'DEPENDENCY_CYCLE: {key}')
                return
            active.add(key)
            for dep in graph[key]: visit(dep)
            active.remove(key); visited.add(key)
        for key in graph: visit(key)
    for task in plan.get('tasks', []):
        for required in ('id','title','kind','objective','references','allowed_paths','deliverables','acceptance','evidence_path'):
            if not task.get(required): errors.append(f'INCOMPLETE_TASK: {task.get("id")} {required}')
        for value in task.get('allowed_paths', [])+task.get('references', [])+[task.get('evidence_path','')]:
            if not safe_relative(value): errors.append(f'UNSAFE_PATH: {task.get("id")} {value}')
        if task.get('initial_status') != 'not_started':
            errors.append(f'FAKE_INITIAL_COMPLETION: {task.get("id")}')
    if trace is not None:
        reqs = trace.get('requirements', [])
        if len(reqs) != trace.get('count'): errors.append('TRACE_COUNT')
        req_ids = [r['id'] for r in reqs]
        if len(req_ids) != len(set(req_ids)): errors.append('TRACE_DUPLICATE')
        for req in reqs:
            if req.get('assigned_task') not in by_id or req.get('verification_task') not in by_id:
                errors.append(f'TRACE_OWNER: {req["id"]}')
            if req.get('execution_status') != 'not_run_in_production':
                errors.append(f'TRACE_FALSE_EXECUTION: {req["id"]}')
    return list(dict.fromkeys(errors))

def evidence_errors(root: Path, task: dict, record: dict) -> list[str]:
    """Check evidence form/integrity, not authenticity of a human or truth of logs."""
    errors: list[str] = []
    if record.get('task_id') != task['id']: errors.append('WRONG_TASK')
    if record.get('result') != 'accepted': errors.append('REPORT_NOT_ACCEPTED')
    review = record.get('review', {})
    if review.get('decision') != 'accepted' or not review.get('reviewer'):
        errors.append('REVIEW_PENDING')
    if review.get('is_synthetic') is not False:
        errors.append('SYNTHETIC_OR_UNKNOWN_REVIEW')
    if task['kind'] in {'human_calibration','human_verification'}:
        if not review.get('evidence') or not review.get('required_role'):
            errors.append('HUMAN_EVIDENCE_MISSING')
    artifacts = record.get('artifacts', [])
    if not artifacts: errors.append('ARTIFACTS_MISSING')
    for artifact in artifacts:
        p = artifact.get('path', '')
        if not safe_relative(p):
            errors.append('ARTIFACT_UNSAFE_PATH'); continue
        fp = root / p
        if not fp.is_file():
            errors.append('ARTIFACT_MISSING'); continue
        expected = artifact.get('sha256')
        if hashlib.sha256(fp.read_bytes()).hexdigest() != expected:
            errors.append('ARTIFACT_HASH')
    for cmd in record.get('commands', []):
        if cmd.get('exit_code') != 0 and not cmd.get('expected_nonzero_proven', False):
            errors.append('COMMAND_FAILED')
    if record.get('blocked_reasons'): errors.append('UNRESOLVED_BLOCK')
    for ar in record.get('acceptance_results', []):
        if ar.get('status') not in {'passed','not_applicable_approved'}:
            errors.append('ACCEPTANCE_NOT_PASSED')
    if not record.get('acceptance_results'): errors.append('ACCEPTANCE_MISSING')
    return list(dict.fromkeys(errors))

def execution_candidates(root: Path, plan: dict, state: dict) -> tuple[set[str], list[str]]:
    candidates: set[str] = set(); errors: list[str] = []
    by_id = {t['id']:t for t in plan['tasks']}
    for ident, entry in state.get('tasks', {}).items():
        if ident not in by_id:
            errors.append('STATE_UNKNOWN_TASK: '+ident); continue
        if entry.get('status') not in ALLOWED_STATUS:
            errors.append('STATE_UNKNOWN_STATUS: '+ident); continue
        if entry.get('status') != 'accepted': continue
        path = entry.get('evidence')
        if not isinstance(path,str) or not safe_relative(path) or not (root/path).is_file():
            errors.append('NO_VALID_EVIDENCE: '+ident); continue
        try: issues = evidence_errors(root, by_id[ident], load_json(root/path))
        except (OSError, ValueError, TypeError) as exc: issues = [str(exc)]
        if issues: errors.append(f'INVALID_EVIDENCE: {ident}: '+','.join(issues))
        else: candidates.add(ident)
    return candidates, errors

def derive_completed(plan: dict, candidates: set[str]) -> tuple[set[str], list[str]]:
    """Candidates already carry evidence. Dependencies still must be accepted."""
    features = plan.get('features', {})
    nodes = plan['tasks']+plan['gates']; done: set[str] = set()
    gate_ids = {g['id'] for g in plan['gates']}
    changed = True
    while changed:
        changed = False
        for node in nodes:
            ident=node['id']
            if ident in done: continue
            if node.get('feature') and not features.get(node['feature'],False): continue
            if ident not in gate_ids and ident not in candidates: continue
            if all(d in done for d in all_dependencies(node,features)):
                done.add(ident); changed=True
    errors=[f'ACCEPTED_WITH_UNMET_DEPENDENCY: {x}' for x in sorted(candidates-done)
            if not next(t for t in plan['tasks'] if t['id']==x).get('feature')
            or features.get(next(t for t in plan['tasks'] if t['id']==x)['feature'],False)]
    return done, errors

def schedule(root: Path, plan: dict, state: dict) -> dict:
    candidates, errors=execution_candidates(root,plan,state)
    done, dependency_errors=derive_completed(plan,candidates);errors+=dependency_errors
    ready=[];feature_disabled=[];waiting=[]
    features=plan.get('features',{})
    for t in plan['tasks']:
        if t.get('feature') and not features.get(t['feature'],False):
            feature_disabled.append(t['id']);continue
        if t['id'] in done:continue
        unmet=[d for d in all_dependencies(t,features) if d not in done]
        status=state.get('tasks',{}).get(t['id'],{}).get('status','not_started')
        if not unmet and status in {'not_started','in_progress'}:
            ready.append(t['id'])
        else:waiting.append({'id':t['id'],'status':status,'unmet':unmet})
    return {'ready':ready,'done':sorted(done),'feature_disabled':feature_disabled,'waiting':waiting,'errors':errors}
