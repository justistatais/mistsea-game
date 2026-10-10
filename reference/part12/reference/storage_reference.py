"""Executable SQLite contract reference, not a Godot/.NET game implementation.

Uses real local files and transactions. Crash hooks deliberately call os._exit
only in isolated test subprocesses. Profile recovery is separate from adventure
rewind; privacy_control.sqlite must not be replaced by an adventure load.
"""
from __future__ import annotations
import hashlib
import json
import os
import sqlite3
import uuid
from pathlib import Path
from typing import Any, Callable

ROOT = Path(__file__).resolve().parents[1]
SNAPSHOT_SCHEMA = 1
BRANCH_TABLES = ('entities','items','balances','receipts','command_log','outbox','draft_heads')

class ContractError(ValueError):
    pass


def canonical(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False)


def digest(value: Any) -> str:
    return hashlib.sha256(canonical(value).encode('utf-8')).hexdigest()


def uid() -> str:
    return uuid.uuid4().hex


def connect(path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(path, timeout=5, isolation_level=None)
    connection.row_factory = sqlite3.Row
    connection.execute('PRAGMA foreign_keys=ON')
    connection.execute('PRAGMA journal_mode=DELETE')
    connection.execute('PRAGMA synchronous=FULL')
    connection.execute('PRAGMA busy_timeout=5000')
    return connection


class Store:
    def __init__(self, directory: str | Path, *, new_session: bool = True):
        self.directory = Path(directory)
        self.directory.mkdir(parents=True, exist_ok=True)
        self.path = self.directory/'profile.sqlite'
        self.control_path = self.directory/'privacy_control.sqlite'
        fresh = not self.path.exists()
        if not fresh and not self.control_path.exists():
            raise ContractError('missing_privacy_control_recovery_required')
        self.db = connect(self.path)
        self.control = connect(self.control_path)
        self.control.executescript((ROOT/'sql/001_privacy_control.sql').read_text())
        if fresh:
            self.db.executescript((ROOT/'sql/001_profile.sql').read_text())
            self.db.executemany('INSERT INTO metadata VALUES(?,?)', [('profile_id','P1'),('load_context',uid())])
            self.db.execute('INSERT INTO branches VALUES(?,?,?,?,?)',('B1',None,None,'REL-0.1',0))
            for id_,kind,payload in [
                ('ferry','vessel',{'docked':True,'bracket_installed':True,'battery_connected':True,'tested_config':7,'config_revision':7}),
                ('player','party',{'scene_id':'R1-S04','anchor_id':'writing-desk'}),
                ('Q-102','quest',{'lifecycle':'active','permission':True,'intent_revision':1,'intent_confirmed':True,'route':'C'})]:
                self.db.execute('INSERT INTO entities VALUES(?,?,?,?,?)',('B1',id_,kind,0,canonical(payload)))
            self.db.executemany('INSERT INTO items VALUES(?,?,?,?,?,?)',[
                ('B1','I-BRACKET','BRACKET','installed','civil-ferry','bracket'),
                ('B1','I-BATTERY','BACKUP-BATTERY','installed','civil-ferry','battery')])
            self.db.executemany('INSERT INTO balances VALUES(?,?,?)', [('B1','xp',340),('B1','coins',140),('B1','personal_parts',3)])
        if self.db.execute('PRAGMA user_version').fetchone()[0] != 1:
            self.close(); raise ContractError('unsupported_database_schema')
        profile = self.meta('profile_id')
        self.control.execute('INSERT OR IGNORE INTO profile_controls VALUES(?,?,?)',(profile,0,0))
        self.reconcile_privacy()
        if new_session:
            self.rotate_context()

    def close(self):
        self.db.close(); self.control.close()

    def meta(self,key: str) -> str:
        row = self.db.execute('SELECT value FROM metadata WHERE key=?',(key,)).fetchone()
        if row is None: raise ContractError('missing_metadata')
        return row[0]

    @property
    def context(self) -> str:
        return self.meta('load_context')

    @property
    def epoch(self) -> int:
        return self.control.execute('SELECT learning_epoch FROM profile_controls WHERE profile_id=?',(self.meta('profile_id'),)).fetchone()[0]

    @property
    def consent_version(self) -> int:
        return self.control.execute('SELECT consent_version FROM profile_controls WHERE profile_id=?',(self.meta('profile_id'),)).fetchone()[0]

    def rotate_context(self):
        self.db.execute('BEGIN IMMEDIATE')
        try:
            self.db.execute('UPDATE metadata SET value=? WHERE key=?',(uid(),'load_context'))
            self.db.execute('DELETE FROM leases')
            self.db.execute('COMMIT')
        except Exception:
            self.db.execute('ROLLBACK');raise

    def entity(self,branch: str,id_: str) -> tuple[int,dict[str,Any]]:
        row=self.db.execute('SELECT revision,payload_json FROM entities WHERE branch_id=? AND entity_id=?',(branch,id_)).fetchone()
        if row is None: raise ContractError('missing_entity')
        return row[0],json.loads(row[1])

    def balance(self,branch: str,account: str) -> int:
        row=self.db.execute('SELECT quantity FROM balances WHERE branch_id=? AND account=?',(branch,account)).fetchone()
        if row is None: raise ContractError('missing_balance')
        return row[0]

    def update_entity(self,branch: str,id_: str,changes: dict[str,Any]):
        """A test fixture for a trusted domain action; not an arbitrary runtime API."""
        rev,payload=self.entity(branch,id_);payload.update(changes)
        self.db.execute('UPDATE entities SET revision=?,payload_json=? WHERE branch_id=? AND entity_id=?',(rev+1,canonical(payload),branch,id_))

    def preview(self,branch: str='B1') -> dict[str,Any]:
        return {'branch':branch,'context':self.context,'release_id':self.db.execute('SELECT release_id FROM branches WHERE branch_id=?',(branch,)).fetchone()[0],
                'expected':{id_:self.entity(branch,id_)[0] for id_ in ('ferry','player','Q-102')},
                'route':'C','type':'DepartFerry'}

    def depart(self,command_id: str,preview: dict[str,Any],hook: Callable[[str],None] | None = None) -> dict[str,Any]:
        if not command_id or len(command_id)>128: raise ContractError('invalid_command_id')
        branch=preview['branch']; request_hash=digest(preview)
        semantic_key='Q-102:civil-ferry:depart'; effect_hash=digest({'type':'DepartFerry','route':preview.get('route')})
        hit=hook or (lambda point: None)
        self.db.execute('BEGIN IMMEDIATE')
        try:
            if preview['context']!=self.context: raise ContractError('stale_load_context')
            prior=self.db.execute('SELECT request_hash,receipt_id FROM command_log WHERE branch_id=? AND command_id=?',(branch,command_id)).fetchone()
            if prior:
                if prior['request_hash']!=request_hash: raise ContractError('command_id_conflict')
                result=self.db.execute('SELECT result_json FROM receipts WHERE branch_id=? AND receipt_id=?',(branch,prior['receipt_id'])).fetchone()[0]
                self.db.execute('COMMIT');return json.loads(result)
            receipt=self.db.execute('SELECT * FROM receipts WHERE branch_id=? AND semantic_key=?',(branch,semantic_key)).fetchone()
            if receipt:
                if receipt['effect_hash']!=effect_hash: raise ContractError('semantic_conflict')
                self.db.execute('INSERT INTO command_log VALUES(?,?,?,?)',(branch,command_id,request_hash,receipt['receipt_id']))
                self.db.execute('COMMIT');return json.loads(receipt['result_json'])
            row=self.db.execute('SELECT release_id FROM branches WHERE branch_id=?',(branch,)).fetchone()
            if row is None or row[0]!=preview['release_id']:raise ContractError('release_mismatch')
            for id_,version in preview['expected'].items():
                if self.entity(branch,id_)[0]!=version: raise ContractError('stale_precondition')
            if set(preview['expected'])!={'ferry','player','Q-102'}:raise ContractError('missing_dependency')
            _,ferry=self.entity(branch,'ferry');_,quest=self.entity(branch,'Q-102')
            required=(ferry.get('docked') is True and ferry.get('bracket_installed') is True and ferry.get('battery_connected') is True
                and ferry.get('tested_config')==ferry.get('config_revision') and isinstance(ferry.get('config_revision'),int)
                and quest.get('permission') is True and quest.get('intent_confirmed') is True and quest.get('route')=='C' and preview.get('route')=='C')
            if not required:raise ContractError('not_ready')
            hit('before_mutation')
            self.update_entity(branch,'ferry',{'docked':False})
            self.update_entity(branch,'player',{'scene_id':'R1-S02','anchor_id':'safe-arrival'})
            self.update_entity(branch,'Q-102',{'lifecycle':'closed'})
            hit('after_world')
            for account,amount in [('xp',160),('coins',60),('personal_parts',2)]:
                self.db.execute('UPDATE balances SET quantity=quantity+? WHERE branch_id=? AND account=?',(amount,branch,account))
            self.db.execute('INSERT INTO items VALUES(?,?,?,?,?,?)',(branch,'I-RE01','RE-01','player','protagonist',None))
            hit('after_rewards')
            receipt_id=uid();result={'receipt_id':receipt_id,'action':'departed','scene_id':'R1-S02','quest':'Q-102'}
            self.db.execute('INSERT INTO receipts VALUES(?,?,?,?,?,?)',(branch,semantic_key,receipt_id,'DepartFerry',effect_hash,canonical(result)))
            self.db.execute('INSERT INTO command_log VALUES(?,?,?,?)',(branch,command_id,request_hash,receipt_id))
            self.db.execute('INSERT INTO outbox VALUES(?,?,?,?,?,?)',(branch,uid(),receipt_id,'FerryDeparturePresentation',canonical({'quest':'Q-102'}),0))
            self.db.execute('UPDATE branches SET sequence=sequence+1 WHERE branch_id=?',(branch,))
            hit('before_commit')
            self.db.execute('COMMIT')
        except Exception:
            if self.db.in_transaction:self.db.execute('ROLLBACK')
            raise
        hit('after_commit_before_ui')
        return result

    def snapshot(self,branch: str='B1') -> str:
        self.db.execute('BEGIN IMMEDIATE')
        try:
            release,seq=self.db.execute('SELECT release_id,sequence FROM branches WHERE branch_id=?',(branch,)).fetchone()
            payload={'schema':1,'profile_id':self.meta('profile_id'),'release_id':release,'tables':{}}
            for table in BRANCH_TABLES:
                rows=[dict(r) for r in self.db.execute(f'SELECT * FROM {table} WHERE branch_id=? ORDER BY rowid',(branch,))]
                payload['tables'][table]=[{k:v for k,v in r.items() if k!='branch_id'} for r in rows]
            id_=uid();self.db.execute('INSERT INTO checkpoints VALUES(?,?,?,?,?,?,?)',(id_,branch,release,1,seq,canonical(payload),digest(payload)))
            self.db.execute('COMMIT');return id_
        except Exception:
            self.db.execute('ROLLBACK');raise

    def fork(self,checkpoint: str,*,installed_releases: set[str] | None = None) -> str:
        installed_releases={'REL-0.1'} if installed_releases is None else installed_releases
        self.db.execute('BEGIN IMMEDIATE')
        try:
            row=self.db.execute('SELECT * FROM checkpoints WHERE checkpoint_id=?',(checkpoint,)).fetchone()
            if row is None:raise ContractError('missing_checkpoint')
            payload=json.loads(row['snapshot_json'])
            if digest(payload)!=row['checksum']:raise ContractError('checkpoint_checksum')
            if payload.get('schema')!=SNAPSHOT_SCHEMA:raise ContractError('unsupported_snapshot')
            if payload.get('profile_id')!=self.meta('profile_id'):raise ContractError('profile_mismatch')
            if payload.get('release_id') not in installed_releases:raise ContractError('missing_release')
            if set(payload['tables'])!=set(BRANCH_TABLES):raise ContractError('snapshot_table_contract')
            branch=uid()
            self.db.execute('INSERT INTO branches VALUES(?,?,?,?,?)',(branch,row['source_branch_id'],checkpoint,row['release_id'],row['sequence']))
            for table in BRANCH_TABLES:
                allowed={x[1] for x in self.db.execute(f'PRAGMA table_info({table})')} - {'branch_id'}
                for values in payload['tables'][table]:
                    if set(values)!=allowed:raise ContractError('snapshot_columns')
                    columns=['branch_id',*values.keys()]
                    self.db.execute(f'INSERT INTO {table}({",".join(columns)}) VALUES({",".join("?" for _ in columns)})',[branch,*values.values()])
            self.db.execute('UPDATE metadata SET value=? WHERE key=?',(uid(),'load_context'))
            self.db.execute('DELETE FROM leases')
            self.db.execute('COMMIT');return branch
        except Exception:
            if self.db.in_transaction:self.db.execute('ROLLBACK')
            raise

    def save_draft(self,branch: str,body: str,*,expected_head: int,context: str) -> tuple[str,int]:
        if len(body.encode('utf-8'))>65536:raise ContractError('draft_size_limit')
        self.db.execute('BEGIN IMMEDIATE')
        try:
            if context!=self.context:raise ContractError('stale_load_context')
            old=self.db.execute('SELECT draft_id,revision FROM draft_heads WHERE branch_id=? AND task_id=?',(branch,'Q-102')).fetchone()
            if (old['revision'] if old else 0)!=expected_head:raise ContractError('draft_cas_conflict')
            old_deleted=bool(old and self.db.execute('SELECT deleted FROM draft_revisions WHERE draft_id=? AND revision=?',(old['draft_id'],old['revision'])).fetchone()[0])
            draft_id=old['draft_id'] if old and not old_deleted else uid()
            rev=self.db.execute('SELECT COALESCE(MAX(revision),0)+1 FROM draft_revisions WHERE draft_id=?',(draft_id,)).fetchone()[0]
            self.db.execute('INSERT INTO draft_revisions VALUES(?,?,?,?,?,?,?)',(draft_id,rev,branch,'0.1',body,digest(body),0))
            self.db.execute('INSERT INTO draft_heads VALUES(?,?,?,?) ON CONFLICT(branch_id,task_id) DO UPDATE SET draft_id=excluded.draft_id,revision=excluded.revision',(branch,'Q-102',draft_id,rev))
            self.db.execute('COMMIT');return draft_id,rev
        except Exception:
            if self.db.in_transaction:self.db.execute('ROLLBACK')
            raise

    def draft(self,branch: str) -> str | None:
        row=self.db.execute('SELECT r.body FROM draft_heads h JOIN draft_revisions r ON h.draft_id=r.draft_id AND h.revision=r.revision WHERE h.branch_id=? AND r.deleted=0',(branch,)).fetchone()
        return row[0] if row else None

    def observe(self,id_: str,attempt: str,family: str='FAMILY-Q102',support: str='assisted'):
        self.db.execute('INSERT OR IGNORE INTO observations VALUES(?,?,?,?,?,?,?)',(id_,self.epoch,'B1',family,attempt,support,canonical({'result':'observed_not_scored'})))

    def request_feedback(self,branch: str) -> str:
        head=self.db.execute('SELECT draft_id,revision FROM draft_heads WHERE branch_id=?',(branch,)).fetchone()
        if not head:raise ContractError('missing_draft')
        id_=uid();self.db.execute('INSERT INTO ai_requests VALUES(?,?,?,?,?,?,?,?,?)',(id_,self.epoch,self.consent_version,branch,self.context,head[0],head[1],'pending',None))
        return id_

    def feedback(self,id_: str,*,active_branch: str) -> str:
        row=self.db.execute('SELECT * FROM ai_requests WHERE request_id=?',(id_,)).fetchone()
        if row is None:return 'discarded'
        if row['profile_epoch']!=self.epoch or row['consent_version']!=self.consent_version:return 'discarded'
        text=self.db.execute('SELECT deleted FROM draft_revisions WHERE draft_id=? AND revision=?',(row['draft_id'],row['draft_revision'])).fetchone()
        if not text or text[0]:return 'discarded'
        if row['branch_id']!=active_branch or row['load_context']!=self.context:status='archived_branch'
        else:
            head=self.db.execute('SELECT draft_id,revision FROM draft_heads WHERE branch_id=?',(active_branch,)).fetchone()
            status='current' if head and (head[0],head[1])==(row['draft_id'],row['draft_revision']) else 'old_draft'
        self.db.execute('UPDATE ai_requests SET status=?,response_json=? WHERE request_id=?',(status,canonical({'status':'unassessed','provider':'test-fixture'}),id_))
        return status

    def revoke_consent(self):
        self.control.execute('UPDATE profile_controls SET consent_version=consent_version+1 WHERE profile_id=?',(self.meta('profile_id'),))

    def erase_learning(self,*,crash_after_control: bool=False):
        profile=self.meta('profile_id');self.control.execute('BEGIN IMMEDIATE')
        try:
            self.control.execute('UPDATE profile_controls SET learning_epoch=learning_epoch+1 WHERE profile_id=?',(profile,))
            self.control.execute('INSERT INTO erasure_jobs VALUES(?,?,?,?,?,?)',(uid(),profile,'learning',None,self.epoch,'pending'))
            self.control.execute('COMMIT')
        except Exception:
            self.control.execute('ROLLBACK');raise
        if crash_after_control:raise ContractError('injected_interruption_after_tombstone')
        self.reconcile_privacy()

    def erase_draft(self,draft_id: str):
        self.control.execute('INSERT INTO erasure_jobs VALUES(?,?,?,?,?,?)',(uid(),self.meta('profile_id'),'draft',draft_id,self.epoch,'pending'))
        self.reconcile_privacy()

    def reconcile_privacy(self):
        """Applied jobs are also enforced after recovery of an older managed DB backup."""
        profile=self.meta('profile_id');epoch=self.epoch
        self.db.execute('BEGIN IMMEDIATE')
        try:
            self.db.execute('DELETE FROM observations WHERE profile_epoch<?',(epoch,))
            self.db.execute('DELETE FROM ai_requests WHERE profile_epoch<?',(epoch,))
            jobs=self.control.execute('SELECT * FROM erasure_jobs WHERE profile_id=?',(profile,)).fetchall()
            for job in jobs:
                if job['scope']=='draft':
                    self.db.execute("UPDATE draft_revisions SET body=NULL,body_hash='erased',deleted=1 WHERE draft_id=?",(job['object_id'],))
                    self.db.execute('DELETE FROM ai_requests WHERE draft_id=?',(job['object_id'],))
            self.db.execute('COMMIT')
        except Exception:
            self.db.execute('ROLLBACK');raise
        self.control.execute("UPDATE erasure_jobs SET status='applied' WHERE profile_id=?",(profile,))

    def acquire(self,branch: str,event: str,resources: list[str]) -> bool:
        self.db.execute('BEGIN IMMEDIATE')
        try:
            for resource in sorted(set(resources)):
                row=self.db.execute('SELECT event_id FROM leases WHERE branch_id=? AND resource_id=?',(branch,resource)).fetchone()
                if row and row[0]!=event:
                    self.db.execute('ROLLBACK');return False
                self.db.execute('INSERT OR IGNORE INTO leases VALUES(?,?,?,?)',(branch,resource,event,self.context))
            self.db.execute('COMMIT');return True
        except Exception:
            self.db.execute('ROLLBACK');raise

    def backup(self,path: str | Path):
        target=Path(path)
        if target.exists():raise ContractError('backup_target_exists')
        destination=sqlite3.connect(target)
        try:self.db.backup(destination)
        finally:destination.close()
        verify=connect(target)
        try:
            if verify.execute('PRAGMA integrity_check').fetchone()[0]!='ok':raise ContractError('backup_integrity')
            if list(verify.execute('PRAGMA foreign_key_check')):raise ContractError('backup_foreign_keys')
        finally:verify.close()


def crash_entry(directory: str,point: str):
    store=Store(directory)
    store.depart('crash-command',store.preview(),lambda phase:os._exit(73) if phase==point else None)
    store.close()

if __name__=='__main__':
    import sys
    if len(sys.argv)==4 and sys.argv[1]=='crash':crash_entry(sys.argv[2],sys.argv[3])
    else:raise SystemExit('Usage: storage_reference.py crash <temporary-profile-dir> <hook-point>')
