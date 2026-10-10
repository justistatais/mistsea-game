"""Engine-independent executable examples for selected Part 04 contracts.
No language assessment, graphics, networking, filesystem transaction, or full game
runtime is implemented. Reward units below count entitlements, not XP or currency.
"""
from __future__ import annotations
from dataclasses import dataclass, replace
from copy import deepcopy
from collections import deque
from typing import Iterable
import uuid

class ContractError(ValueError):
    pass

@dataclass(frozen=True)
class Meaning:
    route: str = 'C'
    reject_a: str = 'closed'
    reject_b: str = 'draught_exceeds_limit'
    repair: str = 'replace_bracket'
    power: str = 'charged_backup_battery'
    generator: str = 'keep_off_until_isolation_confirmed'

    @property
    def admissible(self) -> bool:
        # Only already-confirmable structured meaning; this does NOT parse English.
        return self == Meaning()

@dataclass(frozen=True)
class ExecutionToken:
    context: str
    read_set: tuple

class FerryEngine:
    """In-memory command semantics with revision and save-branch examples."""
    def __init__(self) -> None:
        self.context = uuid.uuid4().hex
        self.accepted = False
        self.permit = True
        self.permission_revision = 1
        self.rules_revision = '0.1'
        self.instance = 'Q-102/campaign-instance-1'
        self.bracket = 'shelf'
        self.mode = 'active'
        self.battery = False
        self.charged = True
        self.config_revision = 0
        self.tested_revision: int | None = None
        self.proposal: Meaning | None = None
        self.proposal_revision = 0
        self.confirmed_revision: int | None = None
        self.docked = True
        self.ferry_revision = 0
        self.closed = False
        self.player_scene = 'R1-S04'
        self.authorized_submit_spot = True  # spatial validity is a dependency
        self.entitlement_count = 0
        self.operation_receipts: dict[str, tuple[ExecutionToken, str]] = {}
        self.settlement_receipt: str | None = None
        self.learning_callbacks: list[dict] = []

    def _editable(self) -> None:
        if self.closed or not self.docked:
            raise ContractError('ferry_not_available')

    def accept(self) -> None:
        self._editable()
        if not self.permit:
            raise ContractError('missing_permission')
        self.accepted = True

    def set_mode(self, mode: str) -> None:
        self._editable()
        if mode not in ('active','focused','isolated','disabled'):
            raise ContractError('unknown_workshop_mode')
        if self.mode == 'disabled' and mode != 'disabled':
            raise ContractError('cannot_respawn_disabled_encounter')
        self.mode = mode

    def take_bracket(self) -> None:
        self._editable()
        if not self.permit or self.mode == 'active':
            raise ContractError('spatial_access_not_established')
        if self.bracket == 'shelf':
            self.bracket = 'carried'
        # A repeated interaction cannot create another instance.

    def install(self) -> None:
        self._editable()
        if self.bracket == 'installed':
            return
        if self.bracket != 'carried':
            raise ContractError('missing_bracket')
        self.bracket = 'installed'
        self.config_revision += 1

    def connect(self, connected: bool) -> None:
        self._editable()
        if self.battery != connected:
            self.battery = connected
            self.config_revision += 1

    def test(self) -> None:
        self._editable()
        if self.bracket != 'installed' or not self.battery or not self.charged:
            raise ContractError('configuration_not_ready')
        self.tested_revision = self.config_revision

    def edit_proposal(self, value: Meaning) -> None:
        self._editable()
        if not isinstance(value, Meaning):
            raise ContractError('untrusted_or_unknown_payload')
        self.proposal = value
        self.proposal_revision += 1
        # Retain the old receipt, but it no longer certifies the current draft.

    def confirm_proposal(self) -> None:
        self._editable()
        if self.proposal is None or not self.proposal.admissible:
            raise ContractError('proposal_requires_clarification')
        self.confirmed_revision = self.proposal_revision

    @property
    def ready(self) -> bool:
        return bool(self.accepted and self.permit and self.docked and not self.closed
                    and self.bracket == 'installed' and self.battery and self.charged
                    and self.tested_revision == self.config_revision
                    and self.proposal is not None and self.proposal.admissible
                    and self.confirmed_revision == self.proposal_revision)

    def _read_set(self) -> tuple:
        return (self.instance,self.rules_revision,self.permission_revision,
                self.config_revision,self.tested_revision,self.proposal_revision,
                self.confirmed_revision,self.ferry_revision)

    def preview(self) -> ExecutionToken:
        if not self.ready or not self.authorized_submit_spot:
            raise ContractError('not_ready_or_invalid_submit_position')
        return ExecutionToken(self.context,self._read_set())

    def submit(self, command_id: str, token: ExecutionToken, *, fail_before_publish=False) -> str:
        if command_id in self.operation_receipts:
            recorded, receipt = self.operation_receipts[command_id]
            if recorded != token:
                raise ContractError('command_id_payload_conflict')
            return receipt
        if token.context != self.context or token.read_set != self._read_set():
            raise ContractError('stale_execution_confirmation')
        if not self.ready or not self.authorized_submit_spot:
            raise ContractError('world_preconditions_changed')
        # Build a complete new in-memory state before publishing it.
        # This is not a proof of crash-safe persistence on disk.
        next_state = deepcopy(self.__dict__)
        receipt = f'{self.instance}/departure'
        next_state.update(docked=False, closed=True, player_scene='R1-S02',
                          entitlement_count=self.entitlement_count+1,
                          ferry_revision=self.ferry_revision+1,
                          settlement_receipt=receipt)
        next_state['operation_receipts'][command_id] = (token,receipt)
        if fail_before_publish:
            raise ContractError('simulated_failure_before_in_memory_publish')
        self.__dict__ = next_state
        return receipt

    def snapshot(self) -> dict:
        return deepcopy(self.__dict__)

    @classmethod
    def load(cls, snapshot: dict) -> 'FerryEngine':
        obj = cls()
        obj.__dict__ = deepcopy(snapshot)
        obj.context = uuid.uuid4().hex # invalidates pending UI commands, not receipts
        return obj

    def language_callback(self, request_id: str, result: str) -> None:
        self.learning_callbacks.append({'request_id':request_id,'result':result})
        # Never call submit() or update world state here.

class ResourceLeases:
    """Exclusive scene/actor claims; inventory reservations are a different owner."""
    def __init__(self) -> None:
        self.holders: dict[str,str] = {}

    def acquire(self, event_id: str, resources: Iterable[str], *, protected_input=False) -> bool:
        wanted = sorted(set(resources))
        if protected_input:
            return False
        if any(self.holders.get(r) not in (None,event_id) for r in wanted):
            return False # do not retain a partial acquisition
        for r in wanted:
            self.holders[r] = event_id
        return True

    def release(self, event_id: str) -> None:
        self.holders = {r:e for r,e in self.holders.items() if e != event_id}

@dataclass(frozen=True)
class AbstractState:
    accepted: bool = False
    mode: str = 'active'
    bracket: str = 'shelf'
    battery: bool = False
    test_current: bool = False
    plan: str = 'none'
    confirmed: bool = False
    departed: bool = False
    entitlements: int = 0

def successors(s: AbstractState):
    if s.departed:
        return []
    out=[]
    def add(name: str, **changes):
        t=replace(s,**changes)
        if t!=s: out.append((name,t))
    if not s.accepted: add('accept',accepted=True)
    if s.mode!='disabled':
        for mode in ('active','focused','isolated','disabled'):
            add('mode_'+mode,mode=mode)
    if s.bracket=='shelf' and s.mode!='active': add('take',bracket='carried')
    if s.bracket=='carried': add('install',bracket='installed',test_current=False)
    add('connect' if not s.battery else 'disconnect',battery=not s.battery,test_current=False)
    if s.bracket=='installed' and s.battery: add('test',test_current=True)
    for plan in ('valid','invalid'):
        if s.plan!=plan: add('draft_'+plan,plan=plan,confirmed=False)
    if s.plan=='valid': add('confirm_meaning',confirmed=True)
    if (s.accepted and s.bracket=='installed' and s.battery and s.test_current
            and s.plan=='valid' and s.confirmed):
        add('confirm_departure',departed=True,entitlements=1)
    return out

def explore() -> dict:
    """Exhaustive reachability of this declared finite abstraction, not the game."""
    start=AbstractState()
    seen={start}; queue=deque([start]); reverse={}; edge_count=0
    while queue:
        state=queue.popleft()
        for action,nxt in successors(state):
            edge_count+=1
            reverse.setdefault(nxt,set()).add(state)
            assert nxt.entitlements in (0,1)
            assert not nxt.departed or (nxt.bracket=='installed' and nxt.battery and nxt.test_current
                                      and nxt.plan=='valid' and nxt.confirmed and nxt.accepted)
            if nxt not in seen:
                seen.add(nxt); queue.append(nxt)
    goals={s for s in seen if s.departed}
    can_finish=set(goals); queue=deque(goals)
    while queue:
        for prev in reverse.get(queue.popleft(),set()):
            if prev not in can_finish:
                can_finish.add(prev);queue.append(prev)
    if seen-can_finish:
        raise AssertionError(f'{len(seen-can_finish)} abstract states have no completion path')
    return {'reachable_states':len(seen),'transitions':edge_count,'terminal_states':len(goals),
            'states_without_completion_path':len(seen-can_finish)}
