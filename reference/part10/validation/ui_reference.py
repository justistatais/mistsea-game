"""Bounded UI contract model, not an engine or a language evaluator.

All state is in memory. DurableStore is a version-checking example, not a claim
of crash-safe disk persistence. See UI_UX_Validation_v0.1.md for actual scope.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Any
import copy

@dataclass(frozen=True)
class KeyEvent:
    key: str
    epoch: int
    control: bool = False
    alt: bool = False
    composing: bool = False
    code: int | None = None
    repeat: bool = False

class InputRouter:
    def __init__(self) -> None:
        self.mode = 'exploration'
        self.epoch = 0
        self.pending: list[str] = []
    def switch(self, mode: str) -> None:
        self.mode = mode
        self.epoch += 1
        self.pending.clear()
    def key(self, event: KeyEvent, editable: bool = False) -> str:
        if event.epoch != self.epoch:
            return 'stale_input_ignored'
        if event.composing or event.code == 229:
            return 'ime_owns_input'
        key = event.key.lower()
        if editable or self.mode == 'editable':
            return 'save_draft' if event.control and key == 's' else 'editor_owns_input'
        if event.repeat and key in {'enter',' ','escape','e'}:
            return 'repeated_command_ignored'
        if self.mode == 'confirmation':
            if key == 'escape': return 'cancel_top_layer'
            return 'focused_dialog_control_only'
        if self.mode in {'transition','combat_resolving'}:
            return 'no_world_command'
        if event.control or event.alt:
            return 'native_or_unbound'
        if key == 'escape': return 'cancel_top_layer'
        if self.mode == 'exploration' and key in {'w','a','s','d','arrowup','arrowleft','arrowdown','arrowright'}:
            return 'move'
        if self.mode in {'exploration','combat_decision'} and key in {'m','j','b','c','l'}:
            return {'m':'map','j':'tasks','b':'archive','c':'party','l':'learning'}[key]
        return 'focused_control_only'
    def release_pointer(self, down_epoch: int, down_target: str, up_target: str) -> bool:
        return down_epoch == self.epoch and down_target == up_target

class Layers:
    def __init__(self) -> None:
        self.stack: list[tuple[str,str]] = []
        self.focus = 'world'
    @property
    def paused(self) -> bool: return bool(self.stack)
    def open(self, layer: str, focus: str = 'cancel') -> None:
        self.stack.append((layer,self.focus))
        self.focus = focus
    def close(self, exists: set[str] | None = None) -> str:
        if not self.stack: return self.focus
        _, target = self.stack.pop()
        self.focus = target if exists is None or target in exists else 'workspace_heading'
        return self.focus

@dataclass(frozen=True)
class SaveTicket:
    document: str
    revision: int
    body: str
    generation: int

class Draft:
    def __init__(self, document: str = 'Q102-DRAFT', generation: int = 1) -> None:
        self.document = document
        self.body = ''
        self.revision = 0
        self.durable_revision = 0
        self.generation = generation
        self.pending: dict[int,SaveTicket] = {}
        self.failed = False
        self.composing = False
        self.intent_revision: int | None = None
    def edit(self, body: str) -> None:
        if body != self.body:
            self.body = body
            self.revision += 1
            self.intent_revision = None
            self.failed = False
    def request_save(self) -> SaveTicket | None:
        if self.composing: return None
        ticket = SaveTicket(self.document,self.revision,self.body,self.generation)
        self.pending[ticket.revision] = ticket
        return ticket
    def acknowledge(self, ticket: SaveTicket) -> bool:
        if ticket.document != self.document or ticket.generation != self.generation:
            return False
        if self.pending.get(ticket.revision) != ticket:
            return False
        self.pending.pop(ticket.revision)
        self.durable_revision = max(self.durable_revision,ticket.revision)
        self.failed = False
        return True
    def fail(self) -> None: self.failed = True
    def confirm_intent(self) -> None: self.intent_revision = self.revision
    @property
    def status(self) -> str:
        if self.failed: return 'error'
        if self.durable_revision == self.revision: return 'saved'
        if self.revision in self.pending: return 'saving'
        return 'dirty'

class DurableStore:
    """Only demonstrates monotonically versioned writes; storage is a dict."""
    def __init__(self) -> None: self.values: dict[str,SaveTicket] = {}
    def write(self, ticket: SaveTicket, fail: bool = False) -> bool:
        if fail: return False
        old = self.values.get(ticket.document)
        if old and (ticket.generation,ticket.revision) < (old.generation,old.revision):
            return False
        if old and (ticket.generation,ticket.revision) == (old.generation,old.revision) and ticket.body != old.body:
            return False
        self.values[ticket.document] = ticket
        return True

@dataclass
class WorldFixture:
    config: int = 0
    tested_config: int | None = None
    bracket: bool = False
    battery: bool = False
    reserve_ok: bool = True
    permission: int = 1
    permission_valid: bool = True
    load_epoch: int = 1
    proposal_revision: int = 1
    confirmed_revision: int | None = None
    route: str | None = None
    generator_off: bool = True
    departed: bool = False
    ui_scale: float = 1.0
    notifications: int = 0
    def configure(self, field: str, value: bool) -> None:
        if field not in {'bracket','battery','reserve_ok'}: raise ValueError('unsupported fixture field')
        if getattr(self,field) != value:
            setattr(self,field,value); self.config += 1
    def test(self) -> bool:
        if self.bracket and self.battery and self.reserve_ok:
            self.tested_config = self.config; return True
        return False
    def relevant(self) -> tuple[Any,...]:
        return (self.config,self.tested_config,self.bracket,self.battery,self.reserve_ok,
                self.permission,self.permission_valid,self.load_epoch,self.proposal_revision,
                self.confirmed_revision,self.route,self.generator_off,self.departed)
    def ready(self) -> bool:
        return (self.bracket and self.battery and self.reserve_ok and self.tested_config == self.config
                and self.permission_valid and self.route == 'C' and self.generator_off
                and self.confirmed_revision == self.proposal_revision and not self.departed)

@dataclass
class ActionPreview:
    snapshot: tuple[Any,...]
    active: bool = True
    def validate(self, state: WorldFixture) -> str:
        if not self.active: return 'closed_preview'
        if self.snapshot != state.relevant(): return 'stale_preview'
        return 'ready' if state.ready() else 'not_ready'
    def close(self) -> None: self.active = False

class ReceiptLedger:
    def __init__(self) -> None:
        self.commands: dict[str, tuple[tuple[str,str], dict[str,Any]]] = {}
        self.goals: dict[str, dict[str,Any]] = {}
        self.reward_count = 0
    def submit(self, goal: str, command: str, payload: str) -> dict[str,Any]:
        signature = (goal, payload)
        if command in self.commands:
            old_payload, receipt = self.commands[command]
            if old_payload != signature: raise ValueError('command_payload_conflict')
            return copy.deepcopy(receipt)
        if goal in self.goals:
            receipt = self.goals[goal]
        else:
            receipt = {'goal':goal,'result':'committed','ordinal':len(self.goals)+1}
            self.goals[goal] = receipt; self.reward_count += 1
        self.commands[command] = (signature,receipt)
        return copy.deepcopy(receipt)
    def query(self, command: str) -> dict[str,Any] | None:
        value = self.commands.get(command)
        return copy.deepcopy(value[1]) if value else None

@dataclass(frozen=True)
class SupportEvent:
    material: str
    scope: str
    kind: str
    seq: int

class SupportLog:
    def __init__(self) -> None: self.events: list[SupportEvent] = []
    def shown(self, material: str, scope: str, kind: str, visible: bool = True) -> None:
        if visible: self.events.append(SupportEvent(material,scope,kind,len(self.events)+1))
    def snapshot(self, material: str, scope: str) -> tuple[SupportEvent,...]:
        return tuple(x for x in self.events if x.material == material and (x.scope == scope or x.scope == '*'))

class Audio:
    def __init__(self, asset_exists: bool) -> None:
        self.asset_exists = asset_exists; self.state = 'ready' if asset_exists else 'missing'
        self.position = 0.0; self.script_shown = False
    def play(self) -> bool:
        if not self.asset_exists: return False
        self.state = 'playing'; return True
    def interrupt(self) -> None:
        if self.state == 'playing': self.state = 'paused'
    def show_script(self) -> str:
        self.script_shown = True; return 'text_assisted' if self.asset_exists else 'reading_script_not_listening'

class FeedbackArchive:
    def __init__(self) -> None:
        self.generation=1; self.entries: list[dict[str,Any]]=[]
    def delete(self) -> None: self.generation += 1; self.entries.clear()
    def receive(self, generation: int, revision: int, current_revision: int, text: str) -> str:
        if generation != self.generation: return 'discarded_deleted_generation'
        category='current_draft' if revision == current_revision else 'older_draft'
        self.entries.append({'revision':revision,'category':category,'text':text})
        return category

def visible_search(records: list[dict[str,str]], allowed: set[str], query: str) -> list[dict[str,str]]:
    # Permission filtering occurs before search, counts and presentation.
    return [dict(r) for r in records if r['id'] in allowed and query.casefold() in (r['title']+' '+r['text']).casefold()]

def layout(width: int, font_scale: float) -> str:
    if width < 1100 or font_scale >= 1.5: return 'single_pane'
    return 'two_pane'

class Notifications:
    def __init__(self) -> None: self.seen:set[str]=set();self.queue:list[str]=[]
    def receive(self, event: str) -> None:
        if event not in self.seen: self.queue.append(event);self.seen.add(event)
    def next(self, protected: bool) -> str | None:
        if protected or not self.queue: return None
        return self.queue.pop(0)
