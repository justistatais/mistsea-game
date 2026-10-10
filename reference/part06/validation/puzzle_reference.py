"""Part06: bounded, engine-independent design checks, NOT an English parser or game.
Run test_puzzles.py from this directory. All proposed results remain read-only;
the quest/world owner must authorise and commit actual actions.
"""
from __future__ import annotations
from dataclasses import dataclass, replace
from enum import Enum
from itertools import permutations, product, combinations
from collections import deque
from typing import Any, Callable, Iterable, Mapping
import json
from pathlib import Path

BASE = Path(__file__).resolve().parents[1]
FIX = json.loads((BASE / 'data/Worked_Logic_Fixtures_v0.1.json').read_text(encoding='utf-8'))

class Truth(Enum):
    TRUE = 'true'
    FALSE = 'false'
    UNKNOWN = 'unknown'


def truth(value: object) -> Truth:
    if value is None:
        return Truth.UNKNOWN
    if type(value) is not bool:
        raise TypeError('Only a declared bool or None is accepted as a world boolean.')
    return Truth.TRUE if value else Truth.FALSE


def all_truth(values: Iterable[Truth]) -> Truth:
    vals = tuple(values)
    if any(v is Truth.FALSE for v in vals):
        return Truth.FALSE
    return Truth.UNKNOWN if any(v is Truth.UNKNOWN for v in vals) else Truth.TRUE


def any_truth(values: Iterable[Truth]) -> Truth:
    vals = tuple(values)
    if any(v is Truth.TRUE for v in vals):
        return Truth.TRUE
    return Truth.UNKNOWN if any(v is Truth.UNKNOWN for v in vals) else Truth.FALSE


@dataclass(frozen=True)
class Check:
    verdict: str
    reasons: tuple[str, ...] = ()


def ferry_route_now(route: str, draught_mm: int | None, pump: bool | None,
                    tested_beacon: bool | None) -> Truth:
    """Current operation only; the scenario fixes other published permissions.
    C planning can be feasible before repairs, but this current-use check cannot.
    """
    if draught_mm is not None and (type(draught_mm) is not int or draught_mm < 0):
        raise ValueError('Draught must be a non-negative integer in millimetres.')
    if route == 'A':
        return Truth.FALSE
    if route == 'B':
        within = Truth.UNKNOWN if draught_mm is None else truth(draught_mm <= FIX['ferry']['b_limit_mm'])
        return all_truth((truth(pump), within))
    if route == 'C':
        return truth(tested_beacon)
    raise ValueError('Route is outside the declared A/B/C domain.')


MEANING_REQUIRED = {
    'route': 'C', 'reject_a': 'closed', 'reject_b': 'draught_exceeds_limit',
    'repair': 'replace_bracket', 'power': 'charged_backup_battery',
    'generator': 'keep_off_until_isolation_confirmed', 'test_before_departure': True,
}


def check_confirmed_meaning(meaning: Mapping[str, object], confirmed: bool) -> Check:
    """Only a user-confirmed semantic object. This does not parse or grade prose."""
    if type(confirmed) is not bool:
        raise TypeError('Confirmation must be a boolean.')
    missing = tuple(k for k in MEANING_REQUIRED if k not in meaning or meaning[k] is None)
    if missing:
        return Check('needs_clarification', missing)
    extra = tuple(k for k in meaning if k not in MEANING_REQUIRED)
    if extra:
        return Check('out_of_scope', extra)
    wrong = tuple(k for k, v in MEANING_REQUIRED.items()
                  if type(meaning[k]) is not type(v) or meaning[k] != v)
    if wrong:
        return Check('contradicted', wrong)
    return Check('feasible' if confirmed else 'needs_clarification', () if confirmed else ('confirm_intent',))


@dataclass(frozen=True)
class Beacon:
    installed: bool = False
    connected: bool = False
    charged: bool | None = True
    configuration_revision: int = 0
    certificate_revision: int | None = None

    def change(self, name: str, value: bool) -> 'Beacon':
        if name not in ('installed', 'connected', 'charged') or type(value) is not bool:
            raise ValueError('Unsupported configuration field/value.')
        if getattr(self, name) is value:
            return self
        return replace(self, **{name: value}, configuration_revision=self.configuration_revision + 1)

    def test(self) -> 'Beacon':
        if self.installed and self.connected and self.charged is True:
            return replace(self, certificate_revision=self.configuration_revision)
        return self  # Real UI reports the observed failure; no certificate is fabricated.

    @property
    def ready(self) -> bool:
        return (self.installed and self.connected and self.charged is True
                and self.certificate_revision is not None
                and self.certificate_revision == self.configuration_revision)


@dataclass(frozen=True)
class Workshop:
    place: str = 'yard'
    mode: str = 'active'
    latch_open: bool = False
    part_taken: bool = False


def workshop_steps(s: Workshop, methods: frozenset[str] = frozenset(('combat', 'isolate', 'focus'))) -> dict[str, Workshop]:
    """Finite access abstraction: authority/basic equipment already present;
    combat action represents a successful external encounter, not combat code.
    """
    out: dict[str, Workshop] = {'wait_or_read': s}
    if s.place == 'yard':
        out['enter_control'] = replace(s, place='control')
        if s.latch_open:
            out['use_shortcut_in'] = replace(s, place='inside')
        if s.mode in ('isolated', 'disabled'):
            out['front_in'] = replace(s, place='inside')
        elif 'combat' in methods:
            out['encounter_success'] = replace(s, mode='disabled', place='inside')
    elif s.place == 'control':
        out['return_yard'] = replace(s, place='yard')
        if s.mode != 'active':
            out['side_in'] = replace(s, place='inside')
        if s.mode != 'disabled':
            if 'isolate' in methods:
                out['select_isolate'] = replace(s, mode='isolated')
            if 'focus' in methods:
                out['select_focus'] = replace(s, mode='focused')
            out['select_normal'] = replace(s, mode='active')
    elif s.place == 'inside':
        if not s.part_taken:
            out['take_part'] = replace(s, part_taken=True)
        if not s.latch_open:
            out['open_latch'] = replace(s, latch_open=True)
        if s.latch_open:
            out['shortcut_out'] = replace(s, place='yard')
        if s.mode != 'active':
            out['side_out'] = replace(s, place='control')
        if s.mode in ('isolated', 'disabled'):
            out['front_out'] = replace(s, place='yard')
    else:
        raise ValueError('Unknown workshop place.')
    return out


def explore(start: Any, stepper: Callable[[Any], Mapping[str, Any]]) -> dict[Any, dict[str, Any]]:
    graph: dict[Any, dict[str, Any]] = {}
    queue = deque([start]); queued = {start}
    while queue:
        s = queue.popleft(); nxt = dict(stepper(s)); graph[s] = nxt
        for t in nxt.values():
            if t not in queued:
                queued.add(t); queue.append(t)
    return graph


def can_finish_from_all(graph: Mapping[Any, Mapping[str, Any]], goal: Callable[[Any], bool]) -> tuple[int, int]:
    reverse: dict[Any, set[Any]] = {s: set() for s in graph}
    for s, out in graph.items():
        for t in out.values():
            reverse[t].add(s)
    good = {s for s in graph if goal(s)}; queue = deque(good)
    while queue:
        s = queue.popleft()
        for prev in reverse[s] - good:
            good.add(prev); queue.append(prev)
    return len(graph), len(graph) - len(good)


def full_workshop_hint(s: Workshop) -> tuple[str, ...]:
    """Current-state recovery, not a cached initial solution. Read-only."""
    goal = lambda t: t.part_taken and t.place == 'yard'
    if goal(s):
        return ()
    queue = deque([(s, ())]); seen = {s}
    # No forced combat in a help path; all states retain the non-combat return.
    while queue:
        x, path = queue.popleft()
        for action, y in workshop_steps(x, frozenset(('isolate', 'focus'))).items():
            if y in seen:
                continue
            if goal(y):
                return path + (action,)
            seen.add(y); queue.append((y, path + (action,)))
    raise ValueError('No safe full hint for this declared state; content recovery required.')


def resolve_booking_arrivals(records: list[dict[str, Any]]) -> dict[str, int]:
    """Explicit identity+field amendments. A later timestamp alone is NOT authority."""
    roots: dict[str, dict[str, Any]] = {}
    for r in records:
        root = r['provenance_root']
        signature = {k: v for k, v in r.items() if k != 'id'}
        if root in roots:
            if signature != {k: v for k, v in roots[root].items() if k != 'id'}:
                raise ValueError('Copies disagree: content_conflict')
        else:
            roots[root] = dict(r)
    original_ids = {r['id']: r['provenance_root'] for r in records}
    original_ids.update({root: root for root in roots})
    by_booking: dict[str, list[str]] = {}
    replaced: set[str] = set()
    for root, r in roots.items():
        by_booking.setdefault(r['booking'], []).append(root)
        if 'supersedes' in r:
            previous = original_ids.get(r['supersedes'])
            if previous is None or roots[previous]['booking'] != r['booking'] or r.get('supersedes_field') != 'arrival':
                raise ValueError('Invalid amendment reference.')
            if r['version'] <= roots[previous]['version']:
                raise ValueError('Invalid amendment revision.')
            replaced.add(previous)
    out: dict[str, int] = {}
    for booking, candidates in by_booking.items():
        active = [root for root in candidates if root not in replaced]
        if len(active) != 1:
            raise ValueError('Unlinked confirmations: content_conflict')
        out[booking] = roots[active[0]]['arrival']
    return out


def hotel_assignments() -> list[dict[str, str]]:
    h = FIX['hotel']; ids = h['bookings']; out = []
    for rooms in permutations(h['rooms'], len(ids)):
        mapping = dict(zip(ids, rooms)); good = True
        for booking, room in mapping.items():
            needs = h['needs'][booking]; actual = h['rooms'][room]
            if actual['beds'] < needs['beds_min']:
                good = False
            for k in ('quiet', 'step_free'):
                if needs.get(k) is True and actual[k] is not True:
                    good = False
        if good:
            out.append(mapping)
    return out


def timeline_orders(before: list[list[str]] | None = None) -> list[tuple[str, ...]]:
    f = FIX['timeline']; constraints = f['before'] if before is None else before
    if any(a not in f['events'] or b not in f['events'] for a, b in constraints):
        raise ValueError('Unknown timeline event.')
    return [p for p in permutations(f['events']) if all(p.index(a) < p.index(b) for a, b in constraints)]


def map_paths(conditions: Mapping[str, bool]) -> list[tuple[str, ...]]:
    f = FIX['map']; out = []
    def walk(node: str, visited: set[str], path: tuple[str, ...]) -> None:
        if node == f['goal']:
            out.append(path); return
        for edge in f['edges']:
            if node not in (edge['a'], edge['b']):
                continue
            if not all(conditions.get(k) is True for k in edge['needs']):
                continue
            nxt = edge['b'] if node == edge['a'] else edge['a']
            if nxt not in visited:
                walk(nxt, visited | {nxt}, path + (edge['id'],))
    walk(f['start'], {f['start']}, ())
    return out


def map_route_check(edge_ids: tuple[str, ...], conditions: Mapping[str, bool | None]) -> Check:
    f = FIX['map']; byid = {e['id']: e for e in f['edges']}
    historical = {e['id'] for e in f['historical_only_edges']}; node = f['start']
    for eid in edge_ids:
        if eid in historical:
            return Check('contradicted', ('historic_crossing_closed',))
        if eid not in byid:
            return Check('out_of_scope', (eid,))
        e = byid[eid]
        if node not in (e['a'], e['b']):
            return Check('contradicted', ('disconnected_route',))
        for need in e['needs']:
            if conditions.get(need) is None:
                return Check('data_unavailable', (need,))
            if conditions[need] is not True:
                return Check('incomplete', (need,))
        node = e['b'] if node == e['a'] else e['a']
    return Check('feasible' if node == f['goal'] else 'incomplete', () if node == f['goal'] else ('destination_not_reached',))


def cargo_check(schedule: Mapping[str, int]) -> Check:
    f = FIX['cargo']; packs = f['packages']
    if set(schedule) - set(packs):
        return Check('out_of_scope', ('unknown_package',))
    if set(packs) - set(schedule):
        return Check('incomplete', ('missing_package',))
    if any(type(t) is not int or t not in f['trips'] for t in schedule.values()):
        return Check('out_of_scope', ('trip',))
    reasons = []
    for trip in f['trips']:
        if sum(packs[k] for k in packs if schedule[k] == trip) > f['capacity_each_trip']:
            reasons.append(f'capacity_trip_{trip}')
    for a, b in f['must_arrive_earlier']:
        if schedule[a] >= schedule[b]:
            reasons.append(f'{a}_not_earlier_than_{b}')
    return Check('contradicted', tuple(reasons)) if reasons else Check('feasible')


def cargo_solutions() -> list[dict[str, int]]:
    f = FIX['cargo']; keys = tuple(f['packages'])
    candidates = (dict(zip(keys, values)) for values in product(f['trips'], repeat=len(keys)))
    return [c for c in candidates if cargo_check(c).verdict == 'feasible']


def comparable_load_pairs() -> list[tuple[str, str]]:
    rows = FIX['trials']['runs']
    return [(a['id'], b['id']) for a, b in combinations(rows, 2)
            if a['reserve'] == b['reserve'] and a['load'] != b['load']]


def evidence_status(support_roots: Iterable[str], contradiction_roots: Iterable[str]) -> str:
    """Roots are admitted, in-scope source roots, not model guesses or filename counts."""
    positive = set(support_roots); negative = set(contradiction_roots)
    if positive and negative:
        return 'content_conflict'
    if positive:
        return 'supported'
    if negative:
        return 'contradicted'
    return 'not_enough_information'


def relevant_preview_current(snapshot: Mapping[str, object], current: Mapping[str, object], keys: tuple[str, ...]) -> Check:
    if any(k not in snapshot or k not in current for k in keys):
        return Check('data_unavailable')
    return Check('feasible' if all(snapshot[k] == current[k] for k in keys) else 'stale')


def finite_beacon_steps(s: tuple[bool, bool, bool]) -> dict[str, tuple[bool, bool, bool]]:
    """Revision freshness abstracted to a bit for exhaustive finite checks."""
    installed, connected, fresh = s
    out = {'read': s}
    if not installed:
        out['install'] = (True, connected, False)
    if not connected:
        out['connect'] = (installed, True, False)
    else:
        out['disconnect'] = (installed, False, False)
    if installed and connected:
        out['test'] = (True, True, True)
    return out


def model_report() -> dict[str, Any]:
    report: dict[str, Any] = {}
    for label, allowed in [('all', frozenset(('combat', 'isolate', 'focus'))), ('combat_only', frozenset(('combat',))), ('isolate_only', frozenset(('isolate',))), ('focus_only', frozenset(('focus',)))]:
        graph = explore(Workshop(), lambda s: workshop_steps(s, allowed))
        states, dead = can_finish_from_all(graph, lambda s: s.part_taken and s.place == 'yard')
        report[label] = dict(states=states,transitions=sum(len(x) for x in graph.values()),states_without_goal_path=dead)
    graph = explore((False, False, False), finite_beacon_steps)
    states, dead = can_finish_from_all(graph, lambda s: s == (True, True, True))
    report['beacon_finite'] = dict(states=states, transitions=sum(len(x) for x in graph.values()),states_without_goal_path=dead)
    report['enumerations'] = dict(hotel_candidates=2,hotel_solutions=len(hotel_assignments()),timeline_candidates=24,timeline_orders=len(timeline_orders()),cargo_candidates=32,cargo_solutions=len(cargo_solutions()),current_map_routes=len(map_paths(dict(credentials_verified=True,basic_tools=True,tunnel_cleared=True))),comparable_load_pairs=comparable_load_pairs())
    report['limits'] = 'Finite contracts only. No geometry, full combat, resource transaction, NLP, audio, IELTS calibration or real save/crash implementation.'
    return report


def trial_claim(claim: str) -> str:
    rows = FIX['trials']['runs']
    if claim == 'observed_40_with_reserve_stable':
        observed = [r for r in rows if r['load'] == 40 and r['reserve']]
        return 'supported' if observed and all(r['result'] == 'stable' for r in observed) else 'not_enough_information'
    if claim == 'all_runs_stable':
        return 'supported' if rows and all(r['result'] == 'stable' for r in rows) else 'contradicted'
    if claim in ('all_six_towers_ready', 'load_55_safe'):
        return 'not_enough_information'  # Explicitly outside the observed domain.
    if claim == 'reserve_unnecessary_in_observed_40_test':
        fail = any(r['load'] == 40 and not r['reserve'] and r['result'] == 'stopped' for r in rows)
        return 'contradicted' if fail else 'not_enough_information'
    raise ValueError('Undeclared claim; do not use an arbitrary fact oracle.')


def compatible_route_conclusions(known: Mapping[str, object]) -> set[frozenset[str]]:
    """Authoring-only information sufficiency example, not the actual ferry state.
    Domain alternatives are explicit hypotheticals. Pump availability and repair
    procedure legality are held fixed. Semantic correspondence still needs review.
    """
    domains = {
        'a_open': (False, True), 'b_limit_mm': (1200, 1600),
        'draught_mm': (1100, 1400), 'c_open': (False, True),
        'beacon_repair_possible': (False, True),
    }
    if any(k not in domains or type(v) is not type(domains[k][0]) or v not in domains[k]
           for k, v in known.items()):
        raise ValueError('Unknown hypothetical fact or value.')
    output: set[frozenset[str]] = set()
    keys = tuple(domains)
    for values in product(*(domains[k] for k in keys)):
        world = dict(zip(keys, values))
        if any(world[k] != v for k, v in known.items()):
            continue
        routes = set()
        if world['a_open']: routes.add('A')
        if world['draught_mm'] <= world['b_limit_mm']: routes.add('B')
        if world['c_open'] and world['beacon_repair_possible']: routes.add('C')
        output.add(frozenset(routes))
    return output
