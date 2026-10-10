"""Part 09 finite reference model; not a game engine, LLM, security boundary or disk store.

All receipts passed here stand for values already checked by their authoritative
quest/consent/roster owners. Production authentication and atomic persistence
belong to Part 12. This model deliberately accepts no language-score input.
"""
from __future__ import annotations
from dataclasses import dataclass, field, asdict
from typing import Any, Iterable, Mapping
import hashlib
import json

class RuleError(ValueError):
    pass

def digest(value: Any) -> str:
    def normal(v: Any) -> Any:
        if hasattr(v, '__dataclass_fields__'):
            return normal(asdict(v))
        if isinstance(v, (set, frozenset)):
            return sorted(normal(x) for x in v)
        if isinstance(v, (tuple, list)):
            return [normal(x) for x in v]
        if isinstance(v, dict):
            return {str(k):normal(v[k]) for k in sorted(v)}
        return v
    return hashlib.sha256(json.dumps(normal(value),sort_keys=True,ensure_ascii=False,separators=(',', ':')).encode()).hexdigest()

@dataclass(frozen=True)
class Consent:
    subject: str
    revision: int
    source_version: str
    fields: frozenset[str]
    recipients: frozenset[str]
    purpose: str
    active: bool = True

@dataclass(frozen=True)
class Disclosure:
    instance: str
    subject: str
    source_version: str
    fields: frozenset[str]
    recipients: frozenset[str]
    purpose: str
    revision: int = 1

@dataclass(frozen=True)
class Preview:
    token: str
    context: str
    payload_digest: str
    consent_revision: int

@dataclass
class DeliveryLedger:
    context: str = 'branch-A:context-1'
    messages: dict[str, dict[str, Any]] = field(default_factory=dict)
    commands: dict[str, tuple[str, str]] = field(default_factory=dict)

    @staticmethod
    def permitted(draft: Disclosure, consent: Consent) -> bool:
        return (consent.active and draft.subject == consent.subject
                and draft.source_version == consent.source_version
                and bool(draft.fields) and draft.fields <= consent.fields
                and bool(draft.recipients) and draft.recipients <= consent.recipients
                and draft.purpose == consent.purpose)

    def preview(self, draft: Disclosure, consent: Consent) -> Preview:
        if not self.permitted(draft, consent):
            raise RuleError('disclosure_scope_not_permitted')
        token=digest((draft, consent.revision, self.context))
        return Preview(token,self.context,digest(draft),consent.revision)

    def commit(self, command: str, draft: Disclosure, consent: Consent,
               preview: Preview, confirmed: bool) -> dict[str, Any]:
        payload=digest(draft)
        if command in self.commands:
            old_payload, key=self.commands[command]
            if old_payload != payload:
                raise RuleError('same_command_different_payload')
            return self.messages[key]
        if not confirmed:
            raise RuleError('explicit_confirmation_required')
        if (preview.context != self.context or preview.payload_digest != payload
            or preview.consent_revision != consent.revision
            or preview.token != digest((draft,consent.revision,self.context))):
            raise RuleError('stale_preview')
        if not self.permitted(draft,consent):
            raise RuleError('disclosure_scope_not_permitted')
        key=f'{draft.instance}@{draft.revision}'
        if key in self.messages:
            existing=self.messages[key]
            if existing['payload_digest'] != payload:
                raise RuleError('same_delivery_slot_different_payload')
            self.commands[command]=(payload,key)
            return existing
        # All checking precedes this in-memory commit. Real crash safety is NOT modeled.
        result={'receipt':key,'payload_digest':payload,'recipients':sorted(draft.recipients),
                'fields':sorted(draft.fields),'purpose':draft.purpose,'consent_revision':consent.revision}
        self.messages[key]=result
        self.commands[command]=(payload,key)
        return result

SAFE_UNKNOWN='目前没有可供核验的相关信息。可以继续查看你已经取得的资料。'

def dialogue_view(facts: Iterable[Mapping[str, Any]], known: set[str],
                  receipts: set[str], private_fact_grants: set[str] | None=None) -> list[dict[str, Any]]:
    """Create a least-information context BEFORE generation. Grants are upstream-validated.
    Does not test natural-language paraphrases, model hallucination or audio assets.
    """
    grants=private_fact_grants or set()
    output=[]
    for fact in facts:
        if fact['id'] not in known or not set(fact['release']) <= receipts:
            continue
        if fact['privacy'] != 'public' and fact['id'] not in grants:
            continue
        output.append({k:fact[k] for k in ('id','text','source','time_scope','kind')})
    return output

def answer_known_topic(topic: str, view: Iterable[Mapping[str, Any]]) -> str:
    """Fixed-template fallback; unknown user guesses are not echoed or confirmed."""
    found=next((f for f in view if f['id']==topic),None)
    return found['text'] if found else SAFE_UNKNOWN

@dataclass
class Relationship:
    work_receipts: set[str] = field(default_factory=set)
    ongoing: bool = False
    private_invited: bool = False
    private_paused: bool = False
    promises: dict[str,str] = field(default_factory=dict)
    repairs: dict[str,str] = field(default_factory=dict)
    positions: dict[str,str] = field(default_factory=dict)
    events: dict[str,str] = field(default_factory=dict)
    history: list[dict[str,str]] = field(default_factory=list)

    def apply(self, receipt: str, kind: str, *, object_id: str='', value: str='',
              confirmed: bool=True, remedy_verified: bool=False) -> None:
        payload=digest((kind,object_id,value,remedy_verified))
        if receipt in self.events:
            if self.events[receipt]!=payload:
                raise RuleError('receipt_conflict')
            return
        allowed={'work_accepted','ongoing_agreement','private_invitation','private_pause',
                 'promise_accepted','promise_paused','promise_fulfilled','promise_renegotiated',
                 'promise_breached','repair_accepted','issue_position'}
        if kind not in allowed:
            raise RuleError('unsupported_relation_effect')
        if not confirmed:
            raise RuleError('unconfirmed_speech_is_not_action')
        if kind=='issue_position' and value not in {'support','reserved','oppose','unknown'}:
            raise RuleError('invalid_issue_position')
        if kind.startswith('promise_') and not object_id:
            raise RuleError('promise_identity_required')
        if kind in {'promise_paused','promise_fulfilled','promise_renegotiated','promise_breached'}:
            if self.promises.get(object_id) not in {'accepted','paused'}:
                raise RuleError('no_open_promise')
        if kind=='promise_accepted' and object_id in self.promises:
            raise RuleError('cannot_reaccept_same_promise')
        if kind=='repair_accepted' and (self.promises.get(object_id)!='breached' or not remedy_verified or not value):
            raise RuleError('specific_remedy_and_acceptance_required')
        if kind=='work_accepted' and not object_id:
            raise RuleError('verified_work_identity_required')
        # Validation ends before mutation.
        if kind=='work_accepted': self.work_receipts.add(object_id)
        elif kind=='ongoing_agreement': self.ongoing=True
        elif kind=='private_invitation': self.private_invited=True; self.private_paused=False
        elif kind=='private_pause': self.private_paused=True
        elif kind=='issue_position': self.positions[object_id]=value
        elif kind=='repair_accepted': self.repairs[object_id]=value
        elif kind.startswith('promise_'):
            self.promises[object_id]={'promise_accepted':'accepted','promise_paused':'paused',
                'promise_fulfilled':'fulfilled','promise_renegotiated':'renegotiated',
                'promise_breached':'breached'}[kind]
        self.events[receipt]=payload
        self.history.append({'receipt':receipt,'kind':kind,'object':object_id,'value':value})

    def view(self) -> dict[str, Any]:
        unresolved=sorted(k for k,v in self.promises.items() if v=='breached' and k not in self.repairs)
        work=('repair_pending' if unresolved else 'ongoing' if self.ongoing
              else 'working' if self.work_receipts else 'unestablished')
        private=('paused' if self.private_paused or (unresolved and self.private_invited)
                 else 'invited' if self.private_invited else 'not_invited')
        return {'work':work,'private':private,'positions':dict(self.positions),
                'unresolved':unresolved,'repairs':dict(self.repairs)}

@dataclass
class Claims:
    holders: dict[str,str] = field(default_factory=dict)
    def acquire(self, owner: str, resources: Iterable[str], *, protected_input: bool=False) -> bool:
        resources=set(resources)
        if protected_input or any(r in self.holders and self.holders[r]!=owner for r in resources):
            return False
        for resource in resources: self.holders[resource]=owner
        return True
    def release(self, owner: str) -> None:
        self.holders={r:o for r,o in self.holders.items() if o!=owner}

@dataclass
class Roster:
    members: set[str] = field(default_factory=set)
    local: set[str] = field(default_factory=set)
    active: list[str] = field(default_factory=list)
    pending_departures: set[str] = field(default_factory=set)
    item_owner: dict[str,str] = field(default_factory=dict)
    location: dict[str,str] = field(default_factory=dict)
    frozen: bool = False

    def recruit(self, actor: str, *, invitation: bool, willing: bool, safe: bool) -> None:
        if not safe or self.frozen: raise RuleError('unsafe_roster_change')
        if not invitation or not willing: raise RuleError('explicit_invitation_and_willingness_required')
        self.members.add(actor);self.local.discard(actor)
        self.location[actor]='HUB-S01'

    def select(self, actors: list[str], claims: Claims) -> None:
        if self.frozen: raise RuleError('frozen_roster')
        if len(actors)>2 or len(set(actors))!=len(actors): raise RuleError('two_unique_slots')
        if not set(actors)<=self.members: raise RuleError('not_recruited')
        if any(f'actor:{a}' in claims.holders for a in actors): raise RuleError('actor_occupied')
        self.active=list(actors)

    def request_departure(self, actor: str) -> None:
        if actor not in self.members: raise RuleError('not_recruited')
        self.pending_departures.add(actor)

    def settle_departures(self, home: Mapping[str,str], *, safe: bool) -> list[str]:
        if not safe or self.frozen: return []
        if any(a not in home for a in self.pending_departures): raise RuleError('safe_home_required')
        settled=sorted(self.pending_departures)
        for actor in settled:
            self.members.remove(actor);self.local.add(actor)
            self.active=[x for x in self.active if x!=actor]
            self.location[actor]=home[actor]
            for item,owner in list(self.item_owner.items()):
                if owner==actor:self.item_owner[item]='collection'
        self.pending_departures.clear()
        return settled

def tactic_tiers(level: int) -> tuple[int,...]:
    if not 1 <= level <= 30: raise RuleError('invalid_level')
    return tuple(t for t in (12,20,24) if level>=t)

def fill_party(named: Iterable[str]) -> tuple[str,...]:
    selected=list(named)
    if len(selected)>2 or len(set(selected))!=len(selected):raise RuleError('two_unique_slots')
    for helper in ('AUX-GUARD','AUX-SUPPORT'):
        if len(selected)<2 and helper not in selected: selected.append(helper)
    return tuple(selected)

def check_service(scope: Mapping[str, Any], receipts: set[str], reserved: set[str]) -> dict[str,Any]:
    missing=sorted(set(scope['required_receipts'])-receipts)
    missing_capacity=sorted(set(scope['required_capacity'])-reserved)
    return {'eligible':not missing and not missing_capacity,'missing_receipts':missing,
            'missing_capacity':missing_capacity,'result_type':scope['result']}

def validate_joint_signatures(rows: Iterable[Mapping[str, Any]], current_protocol: str) -> bool:
    rows=list(rows)
    if len(rows)!=6 or {r['region'] for r in rows}!={f'R{i}' for i in range(1,7)}:return False
    for r in rows:
        if (r['operator']==r['representative']
            or r['operator_scope']!='operate:'+r['region']
            or r['representative_scope']!='represent:'+r['region']
            or r['protocol_version']!=current_protocol
            or not r.get('operator_receipt') or not r.get('representative_receipt')):return False
    receipt_ids=[r[k] for r in rows for k in ('operator_receipt','representative_receipt')]
    return len(set(receipt_ids))==len(receipt_ids)

@dataclass(frozen=True)
class Readiness:
    base_six: bool
    central_stable: bool
    technical_valid: bool=False
    upgrades_installed: bool=False
    supply_ready: bool=False
    agreement_ready: bool=False
    trials_passed: bool=False

def ending_options(readiness: Readiness) -> tuple[str,...]:
    if not readiness.base_six or not readiness.central_stable:return ()
    basic=('limited_central','local_disconnect')
    extra=(readiness.technical_valid and readiness.upgrades_installed and readiness.supply_ready
           and readiness.agreement_ready and readiness.trials_passed)
    return basic+('distributed',) if extra else basic

@dataclass
class RewardLedger:
    claimed: dict[str,tuple[int,int,int]] = field(default_factory=dict)
    xp: int=0
    marks: int=0
    parts: int=0
    def claim(self,node: Mapping[str,Any], verified_receipts: set[str]) -> bool:
        key=node['id']
        expected=(node['xp'],node['marks'],node['parts'])
        if key in self.claimed:
            if self.claimed[key]!=expected:raise RuleError('reward_version_conflict')
            return False
        if 'closed:'+key not in verified_receipts:raise RuleError('not_completed')
        self.claimed[key]=expected
        self.xp+=expected[0];self.marks+=expected[1];self.parts+=expected[2]
        return True
