"""Finite reference contracts, not a production gateway or an English evaluator.
No model/network calls, no world-state mutation, no real identity authentication.
Trusted registries and permission snapshots are passed in by the caller in tests;
Part12 must supply authenticated, persistent implementations.
"""
from __future__ import annotations
from dataclasses import dataclass, asdict
from decimal import Decimal
import hashlib
import json
from typing import Any, Mapping, Sequence


class ContractError(ValueError):
    pass


def canonical_hash(value: Any) -> str:
    return hashlib.sha256(json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':'),allow_nan=False).encode('utf-8')).hexdigest()


def exact_keys(obj: Mapping[str,Any], allowed: set[str], required: set[str]) -> None:
    if not isinstance(obj,dict):
        raise ContractError('object_required')
    if set(obj)-allowed or required-set(obj):
        raise ContractError('unexpected_or_missing_field')


def is_int(x: Any) -> bool:
    return isinstance(x,int) and not isinstance(x,bool)


def validate_target(target: Mapping[str,Any]) -> None:
    exact_keys(target,{'reference','reading','listening','writing','speaking','auto_escalate'}, {'reference','reading','listening','writing','speaking','auto_escalate'})
    if target['reference']!='IELTS Academic' or any(type(target[k]) not in (int,float) or target[k]!=6.5 for k in ('reading','listening','writing')):
        raise ContractError('target_must_be_65')
    if target['speaking'] is not None or target['auto_escalate'] is not False:
        raise ContractError('unsupported_target_policy')


def merge_records(records: Sequence[dict]) -> dict:
    merged={}
    for x in records:
        key=(x['asset_id'],x['version'])
        if key in merged and merged[key]['content_hash']!=x['content_hash']:
            raise ContractError('same_version_different_content')
        merged.setdefault(key,x)
    return merged


def dependency_order(graph: Mapping[str,Sequence[str]]) -> list[str]:
    visiting=set();done=set();order=[]
    def visit(node):
        if node not in graph: raise ContractError('unresolved_dependency')
        if node in visiting: raise ContractError('dependency_cycle')
        if node in done:return
        visiting.add(node)
        for dep in graph[node]: visit(dep)
        visiting.remove(node);done.add(node);order.append(node)
    for n in graph:visit(n)
    return order


def affected_dependents(graph: Mapping[str,Sequence[str]], changed: set[str]) -> set[str]:
    dependency_order(graph)
    if not changed.issubset(graph): raise ContractError('unknown_changed_asset')
    impacted=set(changed)
    while True:
        next_set=impacted|{n for n,deps in graph.items() if impacted.intersection(deps)}
        if next_set==impacted:return impacted
        impacted=next_set


def assessment_fingerprint(bundle: Mapping[str,Any]) -> str:
    # Changes to source, tasks, audio, supports, time/tools and renderer that exposes
    # extra facts cannot retain a prior difficulty attestation automatically.
    keys=('assets','task_rules','support_contract','audio_contract','render_contract','test_conditions','target')
    return canonical_hash({k:bundle[k] for k in keys})


def release_gate(bundle: Mapping[str,Any], attestations: Sequence[dict], trusted_reviewers: Mapping[str,set[str]], channel: str) -> list[str]:
    if channel not in {'internal','player_release'}: raise ContractError('unknown_release_channel')
    errors=[]
    try: validate_target(bundle['target'])
    except ContractError as e: errors.append(str(e))
    try: dependency_order(bundle['dependencies'])
    except ContractError as e: errors.append(str(e))
    if bundle['blocked_issues']:errors.append('unresolved_blocker')
    if bundle['status']=='quarantined': errors.append('bundle_quarantined')
    if channel=='internal':
        if not bundle.get('unassessed_label'):errors.append('internal_label_required')
        return sorted(set(errors))
    if bundle['rights']!='reviewed':errors.append('rights_review_pending')
    for asset in bundle['assets']:
        if asset.get('requires_audio') and asset.get('audio_status')!='auditioned':
            errors.append('required_audio_not_auditioned')
        if asset.get('requires_audio') and asset.get('audio_script_hash')!=asset.get('script_hash'):
            errors.append('audio_script_version_mismatch')
    fp=assessment_fingerprint(bundle)
    valid=[]
    for a in attestations:
        if a.get('fingerprint')!=fp or a.get('decision')!='accept': continue
        if a.get('role') not in trusted_reviewers.get(a.get('reviewer'),set()):continue
        if not a.get('evidence_ref'):continue
        valid.append(a)
    roles={a['role'] for a in valid}
    required={'story','language','puzzle','integration','calibration'}
    if not required.issubset(roles):errors.append('missing_scoped_approval')
    story_ids={a['reviewer'] for a in valid if a['role']=='story'}
    lang_ids={a['reviewer'] for a in valid if a['role']=='language'}
    if not any(s!=l for s in story_ids for l in lang_ids):errors.append('independent_story_language_review_required')
    # This only checks an externally trusted attestation's binding. It does not
    # read participant records, validate exam scores or prove actual calibration.
    return sorted(set(errors))


@dataclass(frozen=True)
class RuntimeContext:
    profile_id:str
    deletion_epoch:int
    consent_revision:int
    permission_revision:int
    branch_id:str
    load_epoch:int
    bundle_id:str
    draft_id:str
    draft_revision:int
    draft_hash:str
    support_scope:str
    model_revision:str
    prompt_revision:str

    def key(self, mode:str, sources:Sequence[dict]) -> str:
        return canonical_hash({'context':asdict(self),'mode':mode,'sources':sources})


def project_sources(catalog:Sequence[dict], authorized:Mapping[str,str], exportable:set[str], *, cloud_consent:bool) -> list[dict]:
    """authorized is a trusted asset_id::segment_id -> exact version projection.
    Rights to view in-game are not equivalent to permission to export to AI.
    Nothing from excluded rows, not even their title, is returned.
    """
    if not cloud_consent: raise ContractError('online_processing_not_consented')
    result=[]
    for row in catalog:
        if authorized.get(f"{row['asset_id']}::{row['segment_id']}")!=row['version']:continue
        if row['asset_id'] not in exportable:continue
        if row.get('audience')!='player_visible':continue
        result.append({'asset_id':row['asset_id'],'version':row['version'],
                       'segment_id':row['segment_id'],'text':row['text']})
    return result


def build_request(ctx:RuntimeContext,mode:str,draft:str,sources:Sequence[dict]) -> dict:
    if mode not in {'explain','feedback','intent_candidate'}:raise ContractError('runtime_feature_disabled')
    if canonical_hash(draft)!=ctx.draft_hash:raise ContractError('draft_hash_mismatch')
    if len(draft)>12000:raise ContractError('select_smaller_revision_scope')
    if len(sources)>6 or sum(len(s['text']) for s in sources)>16000:raise ContractError('context_budget_exceeded')
    # Answers, gold explanations, master plot and world write capabilities are not fields.
    return {'request_id':ctx.key(mode,sources),'mode':mode,'draft':draft,'sources':list(sources),
            'target':6.5,'support_scope':ctx.support_scope,'reply_version':'0.1'}


def validate_citation(citation:dict,sources:Sequence[dict]) -> None:
    fields={'asset_id','version','segment_id','start','end','quote'}
    exact_keys(citation,fields,fields)
    matches=[s for s in sources if all(s[k]==citation[k] for k in ('asset_id','version','segment_id'))]
    if len(matches)!=1:raise ContractError('citation_not_in_projected_sources')
    text=matches[0]['text'];start,end=citation['start'],citation['end']
    if not is_int(start) or not is_int(end) or not 0<=start<end<=len(text):raise ContractError('citation_range_invalid')
    if text[start:end]!=citation['quote']:raise ContractError('citation_quote_mismatch')


def validate_reply(reply:dict,request:dict) -> None:
    fields={'request_id','mode','status','items'}
    exact_keys(reply,fields,fields)
    if reply['request_id']!=request['request_id'] or reply['mode']!=request['mode']:raise ContractError('response_binding_mismatch')
    if reply['status'] not in {'candidate','unassessed','needs_clarification'}:raise ContractError('invalid_status')
    if not isinstance(reply['items'],list) or len(reply['items'])>8:raise ContractError('invalid_item_count')
    if reply['status']!='candidate' and reply['items']:raise ContractError('noncandidate_has_payload')
    if reply['status']=='candidate' and not reply['items']:raise ContractError('candidate_empty')
    if reply['mode']=='intent_candidate' and len(reply['items'])>1:raise ContractError('one_intent_candidate_only')
    for item in reply['items']:
        if reply['mode']=='feedback':
            f={'start','end','quote','category','certainty','explanation','suggestion'}
            exact_keys(item,f,f)
            s,e=item['start'],item['end'];text=request['draft']
            if not is_int(s) or not is_int(e) or not 0<=s<e<=len(text) or text[s:e]!=item['quote']:
                raise ContractError('draft_span_mismatch')
            if item['category'] not in {'meaning','organization','vocabulary','grammar','spelling','style'}:raise ContractError('bad_feedback_category')
            if item['certainty'] not in {'issue','possible','preference'}:raise ContractError('bad_certainty')
            if not isinstance(item['explanation'],str) or len(item['explanation'])>1800:raise ContractError('invalid_explanation')
            if item['suggestion'] is not None and (not isinstance(item['suggestion'],str) or len(item['suggestion'])>1800):raise ContractError('invalid_suggestion')
        elif reply['mode']=='explain':
            f={'text','citation'};exact_keys(item,f,f)
            if not isinstance(item['text'],str) or not item['text'] or len(item['text'])>1800:raise ContractError('invalid_explanation')
            validate_citation(item['citation'],request['sources'])
        else:
            f={'route','main_generator','beacon_plan','unresolved'};exact_keys(item,f,f)
            if item['route'] not in {'A','B','C','unknown'}:raise ContractError('invalid_route')
            if item['main_generator'] not in {'off','on','unspecified'}:raise ContractError('invalid_generator_intent')
            if item['beacon_plan'] not in {'repair_then_test','test_before_departure','already_tested_claim','unspecified'}:raise ContractError('invalid_beacon_intent')
            if not isinstance(item['unresolved'],list) or any(not isinstance(x,str) or len(x)>400 for x in item['unresolved']):raise ContractError('invalid_unresolved')
    # Shape/spans are NOT semantic truth checks. A grammatically bad suggestion
    # or a contradiction with an otherwise correct citation may pass these checks.


def route_delivery(sent:RuntimeContext,current:RuntimeContext,*,still_consented:bool,request_revoked:bool=False,bundle_revoked:bool=False) -> str:
    if not still_consented or request_revoked or bundle_revoked:return 'discard'
    if sent.profile_id!=current.profile_id or sent.deletion_epoch!=current.deletion_epoch:return 'discard'
    if sent.consent_revision!=current.consent_revision or sent.permission_revision!=current.permission_revision:return 'discard'
    if sent.bundle_id!=current.bundle_id or sent.branch_id!=current.branch_id or sent.load_epoch!=current.load_epoch:return 'history_only_no_world_projection'
    if sent.draft_id!=current.draft_id or sent.draft_revision!=current.draft_revision or sent.draft_hash!=current.draft_hash:return 'old_draft_only'
    if sent.support_scope!=current.support_scope:return 'history_only_no_world_projection'
    if sent.prompt_revision!=current.prompt_revision or sent.model_revision!=current.model_revision:return 'history_only_no_world_projection'
    return 'current_feedback_only'


class Budget:
    """Local finite budget example. Real retries get different attempt IDs.
    Transport replays keep the same attempt ID. No actual prices are embedded.
    """
    def __init__(self,limit:str):
        self.limit=Decimal(limit);self.reserved:dict[str,Decimal]={};self.settled:dict[str,Decimal]={};self.spent=Decimal(0)
        if not self.limit.is_finite() or self.limit<0:raise ContractError('invalid_budget')
    def reserve(self,attempt:str,amount:str)->bool:
        value=Decimal(amount)
        if value<0 or not value.is_finite():raise ContractError('invalid_reservation')
        if attempt in self.settled: return False
        if attempt in self.reserved:
            if self.reserved[attempt]!=value:raise ContractError('reservation_payload_conflict')
            return True
        if self.spent+sum(self.reserved.values())+value>self.limit:return False
        self.reserved[attempt]=value;return True
    def settle(self,attempt:str,actual:str)->None:
        value=Decimal(actual)
        if value<0 or not value.is_finite():raise ContractError('invalid_charge')
        if attempt in self.settled:
            if self.settled[attempt]!=value:raise ContractError('charge_payload_conflict')
            return
        if attempt not in self.reserved:raise ContractError('unreserved_charge')
        over=value>self.reserved[attempt]
        self.reserved.pop(attempt);self.settled[attempt]=value;self.spent+=value
        if over:raise ContractError('provider_overage_recorded_circuit_break_required')
    def cancel_unsent(self,attempt:str)->None:
        self.reserved.pop(attempt,None)


def retry_allowed(*,failure:str,attempts:int,consent:bool,budget_ok:bool)->bool:
    return consent and budget_ok and attempts<2 and failure in {'rate_limit','service_error'}


def validate_audio(asset:dict)->None:
    if asset['state'] not in {'script_only','rendered','auditioned'}:raise ContractError('invalid_audio_state')
    if asset['state']=='script_only':
        if asset.get('audio_hash') is not None or asset.get('segments'):raise ContractError('script_claims_audio')
        return
    if not asset.get('audio_hash') or asset.get('duration_ms',0)<=0:raise ContractError('missing_audio_bytes_or_duration')
    if asset.get('rendered_from_hash')!=asset['script_hash']:raise ContractError('audio_from_wrong_script')
    if asset['state']=='auditioned' and not asset.get('listening_review_ref'):raise ContractError('missing_listening_review')
    prev=-1;ids=set()
    for seg in asset['segments']:
        s,e=seg['start_ms'],seg['end_ms']
        if not is_int(s) or not is_int(e) or not 0<=s<e<=asset['duration_ms']:raise ContractError('invalid_audio_range')
        if s<prev:raise ContractError('unreviewed_audio_overlap')
        if seg['id'] in ids:raise ContractError('duplicate_audio_segment')
        ids.add(seg['id']);prev=e
    if set(asset.get('required_segment_ids',[]))!=ids:raise ContractError('audio_text_coverage_mismatch')


def choose_bundle(active_pin:str|None,preferred:str,available:set[str],revoked:set[str]) -> str:
    target=active_pin or preferred
    if target in revoked:raise ContractError('explicit_repair_required')
    if target not in available:raise ContractError('pinned_bundle_missing_no_silent_upgrade')
    return target


def safe_member(path:str)->bool:
    from pathlib import PurePosixPath
    if not path or '\\' in path or '\x00' in path or ':' in path:return False
    p=PurePosixPath(path)
    return not p.is_absolute() and '..' not in p.parts and str(p) not in {'','.'}
