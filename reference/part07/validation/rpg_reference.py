"""Part 07 executable reference, standard library only. Not a combat engine.
All ratios use integer basis points. No model output or learning state can grant rewards.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from fractions import Fraction
from pathlib import Path
from copy import deepcopy
from typing import Mapping, Iterable
import json
ROOT=Path(__file__).resolve().parents[1]
REF=json.loads((ROOT/'data'/'Character_Progression_Reference_v0.1.json').read_text())
SKILLS={s['id']:s for s in REF['skills']}
ITEMS={x['id']:x for x in REF['items']}
ATTRS=('body','strength','agility','perception','focus')

def integer(value:int,name:str,minimum:int=0,maximum:int|None=None)->int:
    if isinstance(value,bool) or not isinstance(value,int): raise TypeError(name+' must be an integer')
    if value<minimum or (maximum is not None and value>maximum): raise ValueError(name+' out of range')
    return value

def threshold(level:int)->int:
    integer(level,'level',1,30)
    n=level-1
    return 100*n+20*n*(n-1)

def level_from_xp(xp:int)->int:
    integer(xp,'xp')
    return max(l for l in range(1,31) if threshold(l)<=xp)

def point_budget(level:int)->int:
    integer(level,'level',1,30)
    return 4+2*(level-1)

def skill_budget(level:int)->int:
    integer(level,'level',1,30)
    return 0 if level<2 else 1+level//2

def valid_stats(level:int, stats:Mapping[str,int])->bool:
    if set(stats)!=set(ATTRS): return False
    try:
        for k,v in stats.items(): integer(v,k,5,25)
        return sum(v-5 for v in stats.values())<=point_budget(level)
    except (ValueError,TypeError): return False

def derived(level:int,stats:Mapping[str,int],grade:int=0,weapon:str='WP-01',armor:str='AR-01',focus_bonus:int=0,armor_bonus_bp:int=0,initiative_bonus:int=0)->dict:
    if not valid_stats(level,stats): raise ValueError('invalid attribute proposal')
    integer(grade,'grade',0,4); integer(focus_bonus,'focus bonus'); integer(armor_bonus_bp,'armor bonus')
    w=ITEMS[weapon]; a=ITEMS[armor]
    if w['kind']!='weapon' or a['kind']!='armor': raise ValueError('wrong equipment slot')
    base=120+20*(level-1)+w['base_weapon_units']+w['per_grade_units']*grade
    def power(scale:str)->Fraction:
        p,s=REF['formula']['power_primary_secondary'][scale]
        return Fraction(base+8*(stats[p]-5)+4*(stats[s]-5),10)
    ini=initiative_bonus+(2 if armor=='AR-03' else -2 if armor=='AR-02' else 0)
    return dict(hp=80+8*(level-1)+6*(stats['body']-5),focus=min(14,6+(stats['focus']-5)//4+focus_bonus),initiative=10+(stats['agility']-5)//2+ini,A=power(w['scaling']),T=power('tech'),armor_bp=min(4500,a['armor_base_bp']+a['per_grade_bp']*grade+armor_bonus_bp),guard_bp=min(3500,2000+50*(stats['body']-5)),exposure_bp=min(4000,500+100*(stats['perception']-5)))

def validate_skills(level:int,selected:Iterable[str],active:Iterable[str]=())->tuple[bool,str]:
    names=list(selected); slots=list(active)
    if len(names)!=len(set(names)): return False,'duplicate talent'
    if any(n not in SKILLS for n in names): return False,'unknown talent'
    if len(names)>skill_budget(level): return False,'over budget'
    if len(slots)>4 or len(slots)!=len(set(slots)): return False,'active slots invalid'
    if any(s not in names or SKILLS[s]['kind']!='active' for s in slots): return False,'unlearned/passive active slot'
    if sum(SKILLS[s]['capstone'] for s in slots)>1: return False,'more than one equipped capstone'
    acquired=set(); pending=set(names)
    while pending:
        advance=False
        for n in sorted(pending):
            row=SKILLS[n]
            spent=sum(SKILLS[a]['tree']==row['tree'] for a in acquired)
            if level>=row['minimum_level'] and set(row['prerequisites'])<=acquired and spent>=row['tree_points_before']:
                acquired.add(n);pending.remove(n);advance=True
        if not advance: return False,'unreachable prerequisites, spend gate, or level gate'
    return True,'ok'

def apply_build(old:dict,level:int,stats:dict,skills:list[str],active:list[str])->dict:
    if not valid_stats(level,stats): raise ValueError('invalid stats')
    valid,why=validate_skills(level,skills,active)
    if not valid: raise ValueError(why)
    result=deepcopy(old)
    result.update(level=level,stats=deepcopy(stats),skills=list(skills),active=list(active))
    result['build_revision']=result.get('build_revision',0)+1
    return result

def damage(power:Fraction,coefficient_bp:int=10000,bonus_bp:int=0,exposure_bp:int=0,armor_bp:int=0,guard_sources:Iterable[int]=())->int:
    integer(coefficient_bp,'coefficient'); integer(bonus_bp,'bonus');integer(exposure_bp,'exposure');integer(armor_bp,'armor')
    values=list(guard_sources)
    for v in values: integer(v,'guard')
    raw=power*Fraction(coefficient_bp,10000)*Fraction(10000+min(5000,bonus_bp),10000)*Fraction(10000+min(4000,exposure_bp),10000)*Fraction(10000-min(4500,armor_bp),10000)*Fraction(10000-min(3500,max(values,default=0)),10000)
    return max(0,raw.numerator//raw.denominator)

def shield(old:int,new:int,max_hp:int)->int:
    integer(old,'old');integer(new,'new');integer(max_hp,'hp',1)
    return min(max_hp//2,max(old,new))

def focus_cost(base:int,reduction:int)->int:
    integer(base,'base');integer(reduction,'reduction')
    return 0 if base==0 else max(1,base-reduction)

def ready_turn(used_turn:int,cooldown:int)->int:
    integer(used_turn,'used');integer(cooldown,'cooldown')
    return used_turn+cooldown+1

@dataclass
class Wallet:
    xp:int=0
    marks:int=100
    parts:int=0
    receipts:set[str]=field(default_factory=set)
    owned:set[str]=field(default_factory=lambda:set(REF['starter_owned']))
    def claim(self, key:str,xp:int,marks:int,parts:int,items:Iterable[str]=())->bool:
        for n,v in [('xp',xp),('marks',marks),('parts',parts)]: integer(v,n)
        names=list(items)
        if any(i not in ITEMS for i in names): raise ValueError('unknown reward item')
        if key in self.receipts:return False
        # All prevalidation precedes mutation. Production implementation also needs durable transactions.
        self.xp+=xp;self.marks+=marks;self.parts+=parts;self.owned.update(names);self.receipts.add(key)
        return True
    def claim_task(self,row:dict)->bool:
        return self.claim(row['entitlement_key'],row['xp'],row['marks'],row['parts'],row['awarded_items'])
    def craft(self,module_id:str,rank:int)->bool:
        row=next((x for x in REF['modules'] if x['id']==module_id),None)
        if row is None: raise ValueError('unknown module')
        if rank<row['minimum_rank']:return False
        if module_id in self.owned:return False
        old=row['consumes_module']
        if old is not None and old not in self.owned:return False
        if self.marks<row['marks_cost'] or self.parts<row['parts_cost']:return False
        self.marks-=row['marks_cost'];self.parts-=row['parts_cost']
        if old:self.owned.remove(old)
        self.owned.add(module_id);return True

@dataclass
class Stock:
    available:dict[str,int]
    held:dict[str,dict[str,int]]=field(default_factory=dict)
    installed:dict[str,dict[str,int]]=field(default_factory=dict)
    def reserve(self,key:str,need:Mapping[str,int])->bool:
        clean=dict(need)
        for k,v in clean.items():integer(v,k,1)
        if key in self.installed:return False
        if key in self.held:return self.held[key]==clean
        if any(self.available.get(k,0)<v for k,v in clean.items()):return False
        for k,v in clean.items():self.available[k]-=v
        self.held[key]=clean;return True
    def commit(self,key:str)->bool:
        if key in self.installed:return False
        if key not in self.held:return False
        self.installed[key]=self.held.pop(key);return True
    def cancel(self,key:str)->bool:
        if key not in self.held:return False
        for k,v in self.held.pop(key).items():self.available[k]=self.available.get(k,0)+v
        return True
    def totals(self)->dict[str,int]:
        out=dict(self.available)
        for group in (self.held,self.installed):
            for items in group.values():
                for k,v in items.items():out[k]=out.get(k,0)+v
        return out

def equip_relics(hero:list[str],companions:list[list[str]],owned:set[str],rank:int)->bool:
    if len(hero)>(2 if rank>=2 else 1):return False
    if any(len(c)>1 for c in companions):return False
    allids=hero+[x for c in companions for x in c]
    return len(allids)==len(set(allids)) and all(x in owned and x in ITEMS and ITEMS[x]['kind']=='relic' for x in allids)

def auto_profile(level:int,weights:Mapping[str,int])->dict[str,int]:
    if set(weights)!=set(ATTRS) or any(type(v)!=int or v<=0 for v in weights.values()):raise ValueError('bad profile')
    bonus={k:0 for k in ATTRS}
    for _ in range(point_budget(level)):
        # Largest next proportional share; deterministic ATTRS tie break, bounded 20.
        available=[k for k in ATTRS if bonus[k]<20]
        target=max(available,key=lambda k:Fraction(weights[k],bonus[k]+1))
        bonus[target]+=1
    return {k:5+bonus[k] for k in ATTRS}

def rank_from_receipts(receipts:set[str])->int:
    # Rank is derived from authoritative canonical quest-result keys, never from an NPC text line.
    rank=0
    for r in REF['ranks'][1:]:
        if r['receipt']+':resolution' in receipts:rank=max(rank,r['rank'])
    return rank
