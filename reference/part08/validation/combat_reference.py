"""Deterministic, bounded combat reference for Part 08, Python standard library.
Not a game engine. See REF['limits'] for exclusions. New combat never calls a
language model and never writes story facts, rewards, or the engineering ledger.
"""
from __future__ import annotations
from dataclasses import dataclass, field, asdict
from fractions import Fraction
from pathlib import Path
from copy import deepcopy
from typing import Any
import json
import progression_reference as P
ROOT=Path(__file__).resolve().parents[1]
REF=json.loads((ROOT/'data/Combat_Reference_v0.1.json').read_text())
ACTIONS={a['id']:a for a in REF['actions']}

@dataclass
class Unit:
    id:str
    side:str
    slot:int
    max_hp:int
    hp:int
    max_focus:int=0
    focus:int=0
    A:Fraction=Fraction(0)
    T:Fraction=Fraction(0)
    armor:int=0
    guard_base:int=2000
    exposure_bonus:int=500
    strength:int=5
    initiative:int=10
    weapon:str='WP-01'
    armor_id:str='AR-01'
    learned:set[str]=field(default_factory=set)
    active:list[str]=field(default_factory=list)
    ap:int=0
    personal_turn:int=0
    eligible_round:int=1
    shield:int=0
    shield_deadline:int=0
    guard:int=0
    guard_deadline:int=0
    guard_effects:dict[str,tuple[int,int]]=field(default_factory=dict)
    next_guard:int=0
    next_guard_deadline:int=0
    exposed:bool=False
    exposed_until_round:int=0
    empowered:int=0
    empowered_deadline:int=0
    outgoing_guard:int=0
    outgoing_deadline:int=0
    next_shield_bonus:int=0
    next_shield_deadline:int=0
    ready:dict[str,int]=field(default_factory=dict)
    turn_used:set[str]=field(default_factory=set)
    encounter_used:set[str]=field(default_factory=set)
    refunds:int=0
    pressure:int=0
    pressure_threshold:int=0
    pending_break:bool=False
    recovery_locked:bool=False
    pressure_round:int=-1
    boss:bool=False
    pattern:list[dict]=field(default_factory=list)
    pattern_index:int=0
    ordinary_debuffs:list[str]=field(default_factory=list)
    @property
    def alive(self)->bool:return self.hp>0

@dataclass
class Trap:
    id:str
    owner:str
    target:str
    coefficient:int
    base_pressure:int
    paid_focus:int

@dataclass
class Intent:
    source:str
    name:str
    damage:int
    targeting:str
    locked_units:tuple[str,...]
    locked_slots:tuple[int,...]
    interruptible:bool=True
    attack:bool=True

@dataclass
class Combat:
    units:dict[str,Unit]
    difficulty:str='standard'
    round:int=0
    marker:int=0
    aid:int=2
    traps:list[Trap]=field(default_factory=list)
    intents:dict[str,Intent]=field(default_factory=dict)
    logs:list[dict]=field(default_factory=list)
    counters:dict[str,int]=field(default_factory=lambda:dict(hp_damage=0,shield_absorbed=0,breaks=0,aid_used=0,focus_spent=0,device_damage=0,downs=0,actions=0))
    mechanism:str|None=None
    controls:dict[str,bool]=field(default_factory=lambda:dict(left=False,right=False,isolate=False))
    sequence:int=0
    outcome:str='active'
    trace_revisions:int=0
    current_actor:str|None=None
    def allies(self,alive:bool=True)->list[Unit]:
        return [u for u in self.units.values() if u.side=='ally' and (u.alive or not alive)]
    def enemies(self,alive:bool=True)->list[Unit]:
        return [u for u in self.units.values() if u.side=='enemy' and (u.alive or not alive)]
    def order(self)->list[str]:
        # Tombstones stay scheduled; downed sources must not freeze expiries.
        return [u.id for side in ['ally','enemy'] for u in sorted([u for u in self.units.values() if u.side==side],key=lambda x:(-x.initiative,x.id))]
    def stamp(self,round_:int,actor:str,end:bool=False)->int:
        return round_*100+2*self.order().index(actor)+(1 if end else 0)
    def future(self,source:Unit,offset:int,end:bool=False)->int:return self.stamp(self.round+offset,source.id,end)
    def log(self,kind:str,**kw:Any)->None:
        self.logs.append(dict(round=self.round,kind=kind,**kw))
    def expire(self,marker:int)->None:
        self.marker=marker
        for u in self.units.values():
            if u.shield and u.shield_deadline<=marker:u.shield=0
            u.guard_effects={k:v for k,v in u.guard_effects.items() if v[1]>marker}
            if u.guard_effects:u.guard=max(v[0] for v in u.guard_effects.values())
            elif u.guard_deadline<=marker:u.guard=0
            if u.next_guard and u.next_guard_deadline<=marker:u.next_guard=0
            if u.empowered and u.empowered_deadline<=marker:u.empowered=0
            if u.outgoing_guard and u.outgoing_deadline<=marker:u.outgoing_guard=0
            if u.next_shield_bonus and u.next_shield_deadline<=marker:u.next_shield_bonus=0
            if u.exposed and self.round>u.exposed_until_round:u.exposed=False
    def start_slot(self,u:Unit)->bool:
        self.expire(self.stamp(self.round,u.id))
        u.personal_turn+=1;u.turn_used.clear();u.ap=0;self.current_actor=u.id
        if not u.alive or u.eligible_round>self.round:return False
        if u.side=='ally':
            u.focus=min(u.max_focus,u.focus+1)
            u.ap=3 if u.id=='hero' else 2
        return True
    def end_slot(self,u:Unit)->None:
        u.ap=0;self.expire(self.stamp(self.round,u.id,True));self.current_actor=None
    def refund(self,u:Unit)->bool:
        if 'refund' in u.turn_used or u.refunds>=2:return False
        u.turn_used.add('refund');u.refunds+=1;u.focus=min(u.max_focus,u.focus+1);return True
    def shield_to(self,source:Unit,target:Unit,coefficient:int,proactive:bool=False)->int:
        if not source.alive or not target.alive:return 0
        bonus=0
        if 'S03' in source.learned:bonus+=1000
        if proactive:
            if source.weapon=='WP-06' and 'wp6_shield' not in source.turn_used:bonus+=1000
            if source.armor_id=='AR-06' and target.id!=source.id:bonus+=1000
            if source.next_shield_bonus:bonus+=source.next_shield_bonus
        new=P.damage(source.T,coefficient,0,0,0,[])
        # Do not round before applying the shield bonus.
        q=source.T*Fraction(coefficient,10000)*Fraction(10000+min(5000,bonus),10000)
        new=q.numerator//q.denominator
        before=target.shield
        target.shield=min(target.max_hp//2,max(target.shield,new))
        target.shield_deadline=max(target.shield_deadline,self.future(source,2))
        self.log('shield',source=source.id,target=target.id,value=target.shield,added=max(0,target.shield-before))
        return target.shield
    def add_guard(self,target:Unit,value:int,deadline:int,key:str)->None:
        target.guard_effects[key]=(min(3500,value),deadline)
        target.guard=max(v[0] for v in target.guard_effects.values())
        target.guard_deadline=max(v[1] for v in target.guard_effects.values())
    def finish_shield_action(self,source:Unit,targets:list[Unit])->None:
        source.turn_used.add('wp6_shield');source.next_shield_bonus=0
        if any(t.id!=source.id for t in targets) and 'S05' in source.learned and 's5' not in source.turn_used:
            source.turn_used.add('s5');self.shield_to(source,source,2000,False)
    def expose(self,target:Unit)->None:
        if target.alive:
            target.exposed=True;target.exposed_until_round=self.round+1
    def knock_down(self,u:Unit)->None:
        u.hp=0;u.shield=0;u.guard=0;u.next_guard=0;u.empowered=0;u.exposed=False;u.outgoing_guard=0;u.next_shield_bonus=0
        u.ordinary_debuffs.clear();u.guard_effects.clear();u.ap=0
        self.traps=[t for t in self.traps if t.owner!=u.id and t.target!=u.id]
        if u.side=='ally':self.counters['downs']+=1
        self.log('down',unit=u.id)
    def pressure_on(self,source:Unit,target:Unit,base:int)->None:
        if base<=0 or not target.alive or target.side!='enemy' or target.recovery_locked:return
        pressure=min(6,int(Fraction(base)*(1+Fraction(2*(source.strength-5),100))))
        if pressure<=0:return
        target.pressure+=pressure;target.pressure_round=self.round
        if target.pressure>=target.pressure_threshold>0:
            target.pressure=0;target.pending_break=True;target.recovery_locked=True
            self.expose(target);self.counters['breaks']+=1;self.log('break',target=target.id,boss=target.boss)
    def hurt(self,source:Unit,target:Unit,power:Fraction,coeff:int=10000,bonus:int=0,exposure:int=0,extra_guards:list[int]|None=None,device:bool=False)->int:
        if not target.alive:return 0
        if target.id=='tower' and self.mechanism=='tower' and not (self.controls['left'] and self.controls['right']):
            self.log('mechanism_locked',target=target.id);return 0
        raw=P.damage(power,coeff,bonus,exposure,target.armor,[target.guard,target.next_guard]+(extra_guards or []))
        absorbed=min(raw,target.shield);target.shield-=absorbed
        actual=max(0,raw-absorbed)
        if target.id=='tower' and self.mechanism=='tower' and not self.controls['isolate']:
            actual=min(actual,max(0,target.hp-1))
        actual=min(actual,target.hp);before=target.hp;target.hp-=actual
        if raw>0:
            target.next_guard=0
            if exposure:target.exposed=False
        if source.side=='ally':
            self.counters['hp_damage']+=actual
            if device:self.counters['device_damage']+=actual
        if target.side=='ally':self.counters['shield_absorbed']+=absorbed
        self.log('damage',source=source.id,target=target.id,hp=actual,absorbed=absorbed,raw=raw,device=device)
        if not target.alive:self.knock_down(target)
        elif 'S08' in target.learned and 's8' not in target.encounter_used and before*2>=target.max_hp and target.hp*2<target.max_hp:
            target.encounter_used.add('s8');self.shield_to(target,target,3500)
        return actual
    def damage_action(self,u:Unit,targets:list[Unit],power_kind:str,coeff:int,pressure:int=0,expose_after:bool=False,pressure_if_exposed:int=0,device:bool=False,skill_id:str='')->None:
        boost=0 if device else u.empowered
        shared_bonus=boost
        if power_kind=='A' and 'H03' in u.learned and not device:shared_bonus+=800
        if power_kind=='T' and 'M03' in u.learned and (device or skill_id!='BASIC'):shared_bonus+=800
        if device and 'M11' in u.learned:shared_bonus+=2000
        if skill_id=='BASIC' and u.weapon=='WP-03' and 'wp3' not in u.encounter_used:shared_bonus+=1500
        for target in targets:
            old_exposed=target.exposed
            exp=0
            if old_exposed:exp=min(4000,u.exposure_bonus+(1000 if 'H05' in u.learned else 0))
            c=coeff+(1500 if skill_id=='H10' and old_exposed and 'H11' in u.learned else 0)
            gated=target.id=='tower' and self.mechanism=='tower' and not(self.controls['left'] and self.controls['right'])
            actual=self.hurt(u,target,u.A if power_kind=='A' else u.T,c,shared_bonus,exp,device=device)
            if not gated:
                self.pressure_on(u,target,pressure+(pressure_if_exposed if old_exposed else 0))
                if expose_after:self.expose(target)
            if old_exposed and actual>0 and 'H09' in u.learned and 'h9' not in u.turn_used:
                u.turn_used.add('h9');u.next_guard=1500;u.next_guard_deadline=self.future(u,1)
        if not device:
            u.empowered=0
            if skill_id=='BASIC':u.encounter_used.add('wp3');u.turn_used.add('basic_attack')
    def cost(self,u:Unit,a:dict)->tuple[int,list[str]]:
        reduction=0;used=[]
        uses_t=a.get('power')=='T' or a['kind'] in ['shield','cleanse_shield','step_shield','step_ally_shield']
        if a['focus']>0 and uses_t and u.weapon=='WP-05' and 'wp5' not in u.encounter_used:
            reduction+=1;used.append('wp5')
        if a['focus']>=3 and 'S12' in u.learned and 's12' not in u.encounter_used:
            reduction+=1;used.append('s12')
        return P.focus_cost(a['focus'],reduction),used
    def can_act(self,u:Unit,sid:str)->bool:
        if sid not in u.active:return False
        a=ACTIONS[sid];cost,_=self.cost(u,a)
        if not u.alive or u.eligible_round>self.round or self.current_actor!=u.id:return False
        if u.ap<a['ap'] or u.focus<cost or u.personal_turn<u.ready.get(sid,0):return False
        if a['kind']=='trap' and len([t for t in self.traps if t.owner==u.id])>=(2 if 'M08' in u.learned else 1):return False
        return True
    def skill(self,u:Unit,sid:str,targets:list[Unit])->bool:
        if not self.can_act(u,sid):return False
        a=ACTIONS[sid];kind=a['kind']
        friendly=kind in ['shield','cleanse_shield','step_shield','step_ally_shield','empower','stance']
        if not targets or len(targets)>a.get('max_targets',1) or len(set(t.id for t in targets))!=len(targets):return False
        if any(not t.alive or (t.side!=u.side if friendly else t.side==u.side) for t in targets):return False
        if kind in ['stance','step_shield'] and [t.id for t in targets]!=[u.id]:return False
        # Full geometry/line-of-sight is outside this reference and checked by COM-AT engine tests.
        fee,tokens=self.cost(u,a);u.ap-=a['ap'];u.focus-=fee;u.encounter_used.update(tokens)
        u.ready[sid]=P.ready_turn(u.personal_turn,a['cd']);self.counters['focus_spent']+=fee;self.counters['actions']+=1
        self.log('skill',actor=u.id,skill=sid,targets=[t.id for t in targets],focus=fee)
        if kind=='damage':
            self.damage_action(u,targets,a['power'],a['coefficient_bp'],a.get('pressure',0),a.get('expose_after',False),a.get('pressure_if_exposed',0),skill_id=sid)
            if a.get('weaken_after_bp'):
                for t in targets:
                    if t.alive:t.outgoing_guard=a['weaken_after_bp'];t.outgoing_deadline=self.future(u,1)
        elif kind in ['shield','cleanse_shield','step_shield','step_ally_shield']:
            co=a['shield_bp']+(1000 if sid in ['S07','S10'] and 'S11' in u.learned else 0)
            for t in targets:
                if kind=='cleanse_shield' and t.ordinary_debuffs:t.ordinary_debuffs.pop(0)
                self.shield_to(u,t,co,True)
                if a.get('guard_bp'):
                    self.add_guard(t,a['guard_bp'],self.future(u,1),sid+':'+u.id)
            self.finish_shield_action(u,targets)
        elif kind=='empower':
            targets[0].empowered=max(targets[0].empowered,a['bonus_bp']);targets[0].empowered_deadline=self.future(u,1,True)
        elif kind=='stance':
            u.empowered=max(u.empowered,2000);u.empowered_deadline=self.future(u,1,True)
            self.add_guard(u,1000,self.future(u,1),sid+':'+u.id)
        elif kind=='trap':
            self.sequence+=1;self.traps.append(Trap(f'trap-{self.sequence}',u.id,targets[0].id,a['coefficient_bp'],a['pressure'],fee))
            if 'M05' in u.learned and 'm5' not in u.turn_used:
                u.turn_used.add('m5');self.shield_to(u,u,2000)
        return True
    def basic(self,u:Unit,target:Unit)->bool:
        if self.current_actor!=u.id or not u.alive or u.ap<2 or not target.alive or u.side==target.side:return False
        u.ap-=2;self.counters['actions']+=1
        base_coefficient=9000 if u.weapon=='WP-02' else 10000
        if u.id!='hero':base_coefficient=base_coefficient*6500//10000
        self.damage_action(u,[target],'A',base_coefficient,1 if u.weapon=='WP-02' else 0,skill_id='BASIC')
        self.log('basic',actor=u.id,target=target.id);return True
    def defend(self,u:Unit)->bool:
        if self.current_actor!=u.id or not u.alive or u.ap<1 or 'defend' in u.turn_used:return False
        u.ap-=1;u.turn_used.add('defend');value=u.guard_base
        if u.weapon=='WP-01' and 'basic_attack' in u.turn_used:value+=500
        if 'H12' in u.learned and u.hp*2<u.max_hp:value+=1000
        self.add_guard(u,value,self.future(u,1),'defend:'+u.id)
        if 'H08' in u.learned and 'h8' not in u.encounter_used:
            u.encounter_used.add('h8');self.refund(u)
        if 'S09' in u.learned:
            u.next_shield_bonus=1500;u.next_shield_deadline=self.future(u,1,True)
        self.counters['actions']+=1;self.log('defend',actor=u.id,guard=u.guard);return True
    def aid_action(self,u:Unit,t:Unit,revive:bool=False)->bool:
        cost=2 if revive else 1
        if self.current_actor!=u.id or not u.alive or u.ap<cost or self.aid<1 or 'supply' in u.turn_used or t.side!=u.side:return False
        if revive and t.alive:return False
        if not revive and (not t.alive or t.hp>=t.max_hp):return False
        self.aid-=1;u.ap-=cost;u.turn_used.add('supply');self.counters['aid_used']+=1;self.counters['actions']+=1
        t.hp=min(t.max_hp,t.hp+int(t.max_hp*(Fraction(1,5) if revive else Fraction(3,10))))
        if revive:t.eligible_round=self.round+1;t.ap=0
        self.log('revive' if revive else 'aid',actor=u.id,target=t.id,hp=t.hp);return True
    def recover_trap(self,u:Unit,trap_id:str)->bool:
        ts=[t for t in self.traps if t.id==trap_id and t.owner==u.id]
        if self.current_actor!=u.id or not u.alive or u.ap<1 or not ts:return False
        t=ts[0];u.ap-=1;self.traps.remove(t)
        if 'M09' in u.learned and 'm9' not in u.encounter_used and t.paid_focus>0:
            u.encounter_used.add('m9');self.refund(u)
        return True
    def operate_tower(self,u:Unit)->bool:
        if self.mechanism!='tower' or self.current_actor!=u.id or u.ap<1 or not u.alive:return False
        # Fixed starting sockets in this bounded scenario. Runtime supports movement and any legal actor.
        operation={0:'left',1:'right',2:'isolate'}.get(u.slot)
        if operation is None or self.controls[operation] or 'local_control' in u.turn_used:return False
        if operation=='isolate' and not(self.controls['left'] and self.controls['right']):return False
        self.controls[operation]=True;u.ap-=1;u.turn_used.add('local_control');self.counters['actions']+=1
        self.log('control',actor=u.id,control=operation);return True
    def declare(self)->None:
        self.intents={}
        live=sorted(self.allies(),key=lambda u:(u.slot,u.id))
        for e in self.enemies():
            p=e.pattern[e.pattern_index%len(e.pattern)]
            locked=tuple(u.id for u in live) if p['target']=='all' else ((live[0].id,) if live else ())
            slots=(0,1,2,3) if p['target']=='all' else ((live[0].slot%2,live[0].slot%2+2) if live and p['target']=='column' else ())
            self.intents[e.id]=Intent(e.id,p['name'],p['damage'],p['target'],locked,slots,p.get('interruptible',True),p.get('attack',True))
        self.log('declare',intents={i.source:dict(name=i.name,target=i.targeting,units=i.locked_units,slots=i.locked_slots) for i in self.intents.values()})
    def trigger_traps(self,e:Unit)->None:
        fired:set[str]=set()
        for trap in list(self.traps):
            if trap not in self.traps or trap.target!=e.id or trap.owner in fired or not e.alive:continue
            owner=self.units[trap.owner]
            if not owner.alive:continue
            self.traps.remove(trap);fired.add(trap.owner)
            self.damage_action(owner,[e],'T',trap.coefficient,trap.base_pressure,device=True)
            if owner.armor_id=='AR-04' and 'ar4' not in owner.encounter_used:
                owner.encounter_used.add('ar4');self.shield_to(owner,owner,2000)
            if 'M12' in owner.learned and 'm12' not in owner.turn_used:
                owner.turn_used.add('m12');self.shield_to(owner,owner,2500)
    def enemy_action(self,e:Unit)->None:
        intent=self.intents.get(e.id)
        if not intent or not e.alive:return
        if e.pending_break and not e.boss:
            e.pending_break=False;e.pattern_index+=1
            self.log('intent_cancelled',enemy=e.id,reason='stagger');return
        if intent.targeting=='unit':initial_targets=[self.units[x] for x in intent.locked_units if self.units[x].alive]
        else:initial_targets=[u for u in self.allies() if u.slot in intent.locked_slots]
        if not initial_targets:
            e.pattern_index+=1
            if not e.pending_break:e.recovery_locked=False
            self.log('intent_cancelled',enemy=e.id,reason='target_gone');return
        if intent.attack:self.trigger_traps(e)
        if not e.alive:return
        if e.pending_break and not e.boss:
            e.pending_break=False;e.pattern_index+=1
            self.log('intent_cancelled',enemy=e.id,reason='trap_stagger');return
        was_broken=e.pending_break
        extra=3500 if e.pending_break and e.boss else 0
        e.pending_break=False
        if intent.targeting=='unit':targets=[self.units[x] for x in intent.locked_units if self.units[x].alive]
        else:targets=[u for u in self.allies() if u.slot in intent.locked_slots]
        if targets and intent.attack:
            scale=next(d['enemy_damage_bp'] for d in REF['difficulty'] if d['id']==self.difficulty)
            for target in targets:self.hurt(e,target,Fraction(intent.damage)*Fraction(scale,10000),extra_guards=[extra,e.outgoing_guard])
            e.outgoing_guard=0
        self.log('enemy_action',enemy=e.id,name=intent.name,targets=[t.id for t in targets],boss_weakened=was_broken and e.boss)
        e.pattern_index+=1
        if e.recovery_locked and not was_broken:e.recovery_locked=False
    def terminal(self)->str:
        # A complete atomic action settles before this check. A wipe does not award a pyrrhic victory.
        if not self.allies():self.outcome='defeat'
        elif not self.enemies() and (self.mechanism!='tower' or all(self.controls.values())):self.outcome='victory'
        return self.outcome
    def snapshot(self)->'Combat':return deepcopy(self)
    def fingerprint(self)->str:
        def norm(v):
            if isinstance(v,Fraction):return [v.numerator,v.denominator]
            if isinstance(v,set):return sorted(v)
            if isinstance(v,dict):return {k:norm(x) for k,x in sorted(v.items())}
            if isinstance(v,(list,tuple)):return [norm(x) for x in v]
            return v
        return json.dumps(norm(asdict(self)),sort_keys=True,ensure_ascii=False)

def actor_from_build(build:dict,grade:int)->Unit:
    ok,why=P.validate_skills(build['level'],build['skills'],build['active'])
    if not ok:raise ValueError(why)
    d=P.derived(build['level'],build['stats'],grade,build['weapon'],build['armor'])
    return Unit('hero','ally',0,d['hp'],d['hp'],d['focus'],d['focus'],d['A'],d['T'],d['armor_bp'],d['guard_bp'],d['exposure_bp'],build['stats']['strength'],d['initiative'],build['weapon'],build['armor'],set(build['skills']),list(build['active']))

def helper(cid:str,level:int,grade:int,slot:int)->Unit:
    spec=next(c for c in REF['companions'] if c['id']==cid)
    stats=P.auto_profile(level,spec['profile']);d=P.derived(level,stats,grade,spec['weapon'],spec['armor'])
    uid='guard' if cid=='AUX-GUARD' else 'support' if cid=='AUX-SUPPORT' else cid
    return Unit(uid,'ally',slot,d['hp'],d['hp'],d['focus'],d['focus'],d['A'],d['T'],d['armor_bp'],d['guard_bp'],d['exposure_bp'],stats['strength'],d['initiative'],spec['weapon'],spec['armor'],set(),spec['native'])

def make_combat(build_id:str,scenario_id:str,difficulty:str='standard')->Combat:
    b=next(x for x in REF['builds'] if x['id']==build_id)
    s=next(x for x in REF['scenarios'] if x['id']==scenario_id)
    if b['level']!=s['level']:raise ValueError('scenario/build reference level mismatch')
    if difficulty not in {d['id'] for d in REF['difficulty']}:raise ValueError('unknown difficulty')
    hero=actor_from_build(b,s['grade'])
    units={u.id:u for u in [hero,helper('AUX-GUARD',s['level'],s['grade'],1),helper('AUX-SUPPORT',s['level'],s['grade'],2)]}
    hp_scale=next(d['enemy_hp_bp'] for d in REF['difficulty'] if d['id']==difficulty)
    for e in s['enemies']:
        hp=max(1,e['hp']*hp_scale//10000)
        u=Unit(e['id'],'enemy',e['slot'],hp,hp,armor=e['armor_bp'],initiative=e['initiative'],pressure_threshold=e['pressure_threshold'],boss=e['boss'],pattern=e['pattern'])
        units[u.id]=u
    return Combat(units=units,difficulty=difficulty,mechanism=s.get('mechanism'))

def pick_targets(c:Combat,u:Unit)->list[Unit]:
    return sorted(c.enemies(),key=lambda e:(e.hp,e.id))

def policy_turn(c:Combat,u:Unit)->None:
    # Deterministic heuristic, not an optimal solver and not the proposed player's mandatory controls.
    for _ in range(8):
        if not u.alive or u.ap<=0 or not c.enemies():return
        down=[a for a in c.allies(False) if not a.alive]
        if down and c.aid_action(u,down[0],True):continue
        critical=sorted([a for a in c.allies() if a.hp*10<a.max_hp*3],key=lambda a:Fraction(a.hp,a.max_hp))
        if critical and c.aid_action(u,critical[0]):continue
        if c.operate_tower(u):continue
        target=pick_targets(c,u)[0]
        live=c.allies();need=sorted(live,key=lambda a:Fraction(a.hp+a.shield,a.max_hp))[0]
        gated=c.mechanism=='tower' and not(c.controls['left'] and c.controls['right'])
        did=False
        # Give committed protection a role; do not fill shields unnecessarily at full safe health.
        threat=c.intents.get(next(iter(c.intents),''))
        needs_shield=need.hp*100<need.max_hp*78 and need.shield<need.T/3
        for sid in ['S10','S07','S01','M06','S06','AUX-SUPPORT-2','AUX-GUARD-2','CP-MIRA-2']:
            if c.can_act(u,sid) and (needs_shield or gated):
                row=ACTIONS[sid];targets=live[:row.get('max_targets',1)] if row.get('max_targets',1)>1 else [need]
                if sid=='S10' and sum(a.hp<a.max_hp*0.8 for a in live)<2 and not gated:continue
                if sid=='S07' and sum(a.hp<a.max_hp*0.8 for a in live)<2 and not gated:continue
                did=c.skill(u,sid,targets)
                if did:break
        if did:continue
        if gated:
            if not c.defend(u):return
            continue
        # Preparation fits beside a two-AP attack, and is skipped at a likely finishing hit.
        if u.ap>=3 and target.hp>u.A:
            if c.can_act(u,'M02') and c.skill(u,'M02',[target]):continue
            if c.can_act(u,'S04'):
                ally=next((a for a in live if a.id=='guard' and not a.empowered),u)
                if not ally.empowered and c.skill(u,'S04',[ally]):continue
            if c.can_act(u,'H02') and c.skill(u,'H02',[u]):continue
        priorities=['M10','H10','M04','H07','M07','H04','H01','M01','S02','AUX-GUARD-1','AUX-SUPPORT-1','CP-LINA-1','CP-MIRA-1','CP-OREN-1','CP-TESS-1']
        for sid in priorities:
            if not c.can_act(u,sid):continue
            row=ACTIONS[sid]
            if sid=='M10' and len(c.enemies())<2 and target.hp<u.T:continue
            if sid=='M04' and len(c.enemies())==1 and c.can_act(u,'M07'):continue
            if sid=='H10' and target.hp<u.A:continue
            if c.skill(u,sid,pick_targets(c,u)[:row.get('max_targets',1)]):did=True;break
        if did:continue
        if c.basic(u,target):continue
        if c.defend(u):continue
        return

def advance_round(c:Combat)->None:
    if c.outcome!='active':return
    c.round+=1;c.expire(c.round*100);c.declare()
    for uid in c.order():
        u=c.units[uid]
        playable=c.start_slot(u)
        if playable:
            if u.side=='ally':policy_turn(c,u)
            else:c.enemy_action(u)
        c.end_slot(u)
        if c.terminal()!='active':return
    for e in c.enemies():
        if not e.recovery_locked and e.pressure_round!=c.round:e.pressure=max(0,e.pressure-1)
    c.expire((c.round+1)*100-1)

def simulate(build_id:str,scenario_id:str,difficulty:str='standard',max_rounds:int=25)->tuple[dict,Combat]:
    c=make_combat(build_id,scenario_id,difficulty)
    while c.round<max_rounds and c.outcome=='active':advance_round(c)
    result=dict(build=build_id,scenario=scenario_id,difficulty=difficulty,outcome=c.outcome if c.outcome!='active' else 'budget_exceeded',rounds=c.round,
                party_hp=sum(u.hp for u in c.allies(False)),party_max_hp=sum(u.max_hp for u in c.allies(False)),**c.counters)
    return result,c

@dataclass
class CoreGate:
    """Mechanism state reference only, not the final boss damage/party simulation."""
    max_hp:int=1900
    hp:int=1900
    phase:int=1
    revision:int=0
    checked_revision:int=-1
    flags:set[str]=field(default_factory=set)
    free_aid:int=2
    focus:int=6
    used_once:set[str]=field(default_factory=set)
    phase_barriers:int=0
    def operate(self,action:str)->bool:
        required={1:('isolate_left','isolate_right'),2:('disable_relay_a','disable_relay_b','latch_bypass'),3:('set_damping','arm_controlled_stop')}[self.phase]
        if action=='verify':
            # Phase 3 arms stop only after checking the damping configuration.
            check_needed=('set_damping',) if self.phase==3 else required
            if not set(check_needed)<=self.flags:return False
            self.checked_revision=self.revision;self.settle_phase();return True
        if action not in required:return False
        if action in self.flags:return False
        if action=='latch_bypass' and not {'disable_relay_a','disable_relay_b'}<=self.flags:return False
        if action=='arm_controlled_stop':
            if 'set_damping' not in self.flags or self.checked_revision!=self.revision:return False
            self.flags.add(action)
            if self.hp<=1:self.hp=0
            return True  # Authorizes the checked configuration; does not rewire it.
        self.flags.add(action);self.revision+=1;return True
    def ready(self)->bool:
        req={1:{'isolate_left','isolate_right'},2:{'disable_relay_a','disable_relay_b','latch_bypass'},3:{'set_damping','arm_controlled_stop'}}[self.phase]
        return req<=self.flags and self.checked_revision==self.revision
    def settle_phase(self)->None:
        floor={1:self.max_hp*70//100,2:self.max_hp*35//100}.get(self.phase)
        if floor is not None and self.hp==floor and self.ready():
            self.phase+=1;self.flags.clear();self.revision+=1;self.checked_revision=-1;self.phase_barriers+=1
    def hit(self,amount:int)->int:
        if isinstance(amount,bool) or not isinstance(amount,int) or amount<0:raise ValueError('nonnegative integer damage required')
        before=self.hp
        if self.phase==1 and not self.ready():return 0
        floor={1:self.max_hp*70//100,2:self.max_hp*35//100,3:0 if self.ready() else 1}[self.phase]
        self.hp=max(floor,self.hp-amount)
        self.settle_phase()
        return before-self.hp
    def invalidate(self)->None:
        self.revision+=1;self.flags.discard('arm_controlled_stop')
    @property
    def won(self)->bool:return self.phase==3 and self.hp==0 and self.ready()

@dataclass
class ReceiptLedger:
    """In-memory illustration: production durability remains Part 12 work."""
    receipts:dict[str,dict]=field(default_factory=dict)
    def commit(self,key:str,outcome:str,objective_ok:bool,living:int,payload:str)->bool:
        if outcome!='victory' or not objective_ok or living<1:return False
        if key in self.receipts:
            if self.receipts[key]['payload']!=payload:raise ValueError('conflicting duplicate outcome')
            return False
        self.receipts[key]=dict(outcome=outcome,payload=payload,kill_xp=0);return True
