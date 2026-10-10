"""Reference logic only: support attribution and learning records, not an English grader.

Python standard library; no network, game-state writes, language-model calls or
IELTS band prediction. The caller must supply valid, reviewed observations.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import Literal, Optional
import math

Mode = Literal['reading','listening','writing']
Outcome = Optional[Literal['correct','incorrect']]

@dataclass(frozen=True)
class Support:
    kind: str
    at_ms: int
    targets: Optional[frozenset[str]] = None  # None = unknown scope, not "irrelevant".

@dataclass(frozen=True)
class Observation:
    id: str
    goal: str
    mode: Mode
    family: str
    context: str
    session: str
    day: float
    outcome: Outcome
    submitted_at_ms: int = 100
    supports: tuple[Support,...] = ()
    assessment_state: str = 'assessed'
    scope_sufficient: bool = True
    response_nonempty: bool = True
    conditions_complete: bool = True
    audio_delivered: bool = True
    audio_replays: int = 0
    audio_paused: bool = False
    speed_changed: bool = False
    answer_seen_before: bool = False
    family_seen_before: bool = False
    clock_trusted: bool = True
    response_version: str = 'v1'
    evaluator_version: str = 'reference-v1'

    def __post_init__(self):
        if self.mode not in {'reading','listening','writing'}:
            raise ValueError('Unsupported modality')
        if self.outcome not in {'correct','incorrect',None}:
            raise ValueError('Unknown result; do not coerce to a grade')
        if self.audio_replays < 0 or not math.isfinite(self.day):
            raise ValueError('Invalid observation time or replay count')


def condition_class(o: Observation) -> str:
    """Classify conditions, not English ability. Reading the source is not a hint."""
    if o.assessment_state != 'assessed' or o.outcome is None:
        return 'unassessed'
    if not o.response_nonempty or not o.scope_sufficient:
        return 'insufficient_evidence'
    if not o.conditions_complete:
        return 'conditions_unknown'
    applicable=[]
    for h in o.supports:
        if h.at_ms > o.submitted_at_ms or h.kind == 'ui':
            continue
        if h.targets is None:
            return 'support_scope_unknown'
        if o.goal in h.targets:
            applicable.append(h)
    if o.mode == 'listening' and not o.audio_delivered:
        return 'text_path_no_audio_evidence'
    if o.answer_seen_before:
        return 'prior_answer_exposure'
    if o.mode == 'listening' and any(h.kind in {'transcript','translation'} for h in applicable):
        return 'text_mediated_listening'
    if applicable:
        return 'supported'
    if o.family_seen_before:
        return 'independent_repeat_material'
    if o.mode == 'listening' and (o.audio_replays or o.audio_paused or o.speed_changed):
        return 'independent_audio_review'
    return 'independent_first_conditions'


INDEPENDENT_NEW = {'independent_first_conditions','independent_audio_review'}
SUPPORTED = {'supported','text_mediated_listening','prior_answer_exposure',
             'support_scope_unknown','text_path_no_audio_evidence'}

class ObservationLedger:
    """A new evaluator interpretation replaces an observation; it is not another attempt."""
    def __init__(self):
        self._records: dict[str,Observation] = {}
        self.deleted = False

    def upsert(self, o: Observation) -> None:
        if self.deleted:
            raise ValueError('Profile deleted; discard delayed response')
        old=self._records.get(o.id)
        if old:
            immutable=('goal','mode','family','context','session','day','response_version','submitted_at_ms')
            if any(getattr(old,k)!=getattr(o,k) for k in immutable):
                raise ValueError('Attempt identity changed: use a real new observation')
        self._records[o.id]=o

    def delete(self) -> None:
        self._records.clear(); self.deleted=True

    def records(self) -> list[Observation]:
        return list(self._records.values())


def profile(records: list[Observation], goal: str, mode: Mode) -> dict:
    """Transparent product heuristic. No band score, no probabilities, no world state."""
    obs=sorted((o for o in records if o.goal==goal and o.mode==mode),key=lambda o:(o.day,o.submitted_at_ms,o.id))
    new=[o for o in obs if condition_class(o) in INDEPENDENT_NEW]
    success=[o for o in new if o.outcome=='correct']
    # One source family is one diversity unit even if several observations are recorded.
    families={o.family for o in success}
    sessions={o.session for o in success}
    contexts={o.context for o in success}
    repeated=len(families)>=3 and len(sessions)>=2 and len(contexts)>=2
    trusted=[o for o in success if o.clock_trusted]
    delayed=repeated and any(b.day-a.day>=7 and a.family!=b.family for a in trusted for b in trusted)
    # Two distinct recent new-family difficulties trigger advice, not deletion of history.
    latest_by_family={o.family:o for o in new}
    latest=sorted(latest_by_family.values(),key=lambda o:(o.day,o.submitted_at_ms,o.id))[-2:]
    recheck=len(latest)==2 and all(o.outcome=='incorrect' for o in latest)
    supported=any(o.outcome=='correct' and condition_class(o) in SUPPORTED for o in obs)
    practiced=any(o.outcome=='correct' and condition_class(o)=='independent_repeat_material' for o in obs)
    status=('delayed_transfer' if delayed else 'repeated_independent' if repeated else
            'independent_once' if success else 'supported' if supported else
            'repeat_practice_only' if practiced else 'insufficient_evidence')
    if recheck: status='needs_recheck'
    return {'goal':goal,'mode':mode,'status':status,'distinct_success_families':len(families),
            'distinct_sessions':len(sessions),'distinct_contexts':len(contexts),
            'has_independent_success':bool(success),'has_delayed_transfer':delayed,
            'needs_recheck':recheck,'band_score':None}


def next_review(stage: int, result: str, now_day: float, existing_due: float | None=None) -> tuple[int,float | None]:
    """Advisory stage changes. Timings are configurable project heuristics, not science claims."""
    intervals=(1,3,7,14,30)
    if stage not in range(len(intervals)) or not math.isfinite(now_day):
        raise ValueError('Invalid schedule input')
    if result=='clean_new':
        stage=min(stage+1,len(intervals)-1)
        return stage,now_day+intervals[stage]
    if result=='incorrect':
        return max(0,stage-1),now_day+1
    if result=='supported':
        return stage,min(existing_due,now_day+1) if existing_due is not None else now_day+1
    if result in {'repeat','unassessed'}:
        return stage,existing_due
    if result=='exposure':
        return stage,now_day+1 if existing_due is None else existing_due
    raise ValueError('Unrecognised review result')

@dataclass(frozen=True)
class ReviewCandidate:
    id: str
    goal: str
    target_band: float = 6.5
    story_compatible: bool = True
    access_allowed: bool = True
    asset_available: bool = True
    requested: bool = False
    repeated_difficulty: bool = False
    due_day: float = 0


def select_reviews(candidates: list[ReviewCandidate], now_day: float, input_protected: bool,
                   declined_ids: frozenset[str]=frozenset(), maximum: int=3) -> list[str]:
    if maximum<0 or not math.isfinite(now_day):
        raise ValueError('Invalid selection settings')
    if input_protected: return []
    eligible=[c for c in candidates if c.target_band==6.5 and c.story_compatible
              and c.access_allowed and c.asset_available and c.id not in declined_ids
              and (c.due_day<=now_day or c.requested or c.repeated_difficulty)]
    eligible.sort(key=lambda c:(not c.requested,not c.repeated_difficulty,c.due_day,c.id))
    selected=[]; seen_ids=set();seen_goals=set()
    for c in eligible:
        if c.id in seen_ids or c.goal in seen_goals: continue
        selected.append(c.id);seen_ids.add(c.id);seen_goals.add(c.goal)
        if len(selected)>=maximum: break
    return selected if maximum else []


def validate_feedback(payload: dict, expected_observation: str, expected_text_version: str,
                      current_profile_deleted: bool=False) -> bool:
    """Schema/authority example only. It does not establish semantic correctness."""
    allowed={'observation_id','text_version','evaluator_version','feedback','status','goal_refs'}
    if current_profile_deleted or not set(payload).issubset(allowed): return False
    return (payload.get('observation_id')==expected_observation and
            payload.get('text_version')==expected_text_version and
            payload.get('status') in {'assessed','unassessed','insufficient_evidence'} and
            isinstance(payload.get('feedback',[]),list))
