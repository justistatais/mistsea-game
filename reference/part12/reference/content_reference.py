"""Bounded local content checks. Does not implement signatures or language calibration."""
from __future__ import annotations
import json
import hashlib
import ntpath
import unicodedata
from pathlib import PurePosixPath
from typing import Any
from storage_reference import ContractError, digest

def safe_path(path: str) -> str:
    if not isinstance(path,str) or not path or '\\' in path or '\x00' in path or ':' in path:
        raise ContractError('unsafe_path')
    normalized=unicodedata.normalize('NFC',path)
    parts=normalized.split('/')
    if PurePosixPath(normalized).is_absolute() or any(p in ('','.','..') for p in parts):
        raise ContractError('unsafe_path')
    reserved={'CON','PRN','AUX','NUL',*(f'COM{i}' for i in range(1,10)),*(f'LPT{i}' for i in range(1,10))}
    if any(p.rstrip(' .')!=p or p.split('.')[0].upper() in reserved for p in parts):raise ContractError('unsafe_path')
    if PurePosixPath(normalized).suffix.lower() not in {'.json','.txt','.ogg','.wav','.png','.webp'}:
        raise ContractError('forbidden_asset_type')
    return normalized

def parse_strict(raw: str) -> Any:
    def hook(pairs):
        obj={}
        for key,value in pairs:
            if key in obj:raise ContractError('duplicate_json_key')
            obj[key]=value
        return obj
    def constant(_):raise ContractError('non_finite_number')
    return json.loads(raw,object_pairs_hook=hook,parse_constant=constant)

def validate_manifest(manifest: dict[str,Any], files: dict[str,bytes], *, channel: str='internal') -> None:
    allowed={'schema_version','release_id','rules_version','target','assets','required_audio','calibration_status','review_status'}
    if set(manifest)!=allowed or manifest['schema_version']!=1:raise ContractError('manifest_contract')
    if manifest['target']!={'exam':'IELTS Academic','reading':6.5,'listening':6.5,'writing':6.5,'speaking':None}:raise ContractError('target_mismatch')
    seen=set(); declared=set()
    for asset in manifest['assets']:
        if set(asset)!={'path','sha256','size','definition_id','version'}:raise ContractError('asset_contract')
        path=safe_path(asset['path']);case=path.casefold()
        if case in seen:raise ContractError('path_collision')
        seen.add(case);declared.add(path)
        raw=files.get(path)
        if raw is None or len(raw)!=asset['size'] or hashlib.sha256(raw).hexdigest()!=asset['sha256']:raise ContractError('asset_integrity')
        if len(raw)>32*1024*1024:raise ContractError('asset_size_limit')
    if set(files)!=declared:raise ContractError('undeclared_file')
    if channel=='public':
        if manifest['review_status']!='approved' or manifest['calibration_status']!='calibrated_for_declared_conditions':raise ContractError('release_not_qualified')
        if any(x not in declared for x in manifest['required_audio']):raise ContractError('missing_required_audio')
    # Real release also requires verified publisher signature & authenticated review authority.

def migrate_candidate(db, *, fail: bool=False):
    """Exercise transactional SQL migration on a COPY; caller retains original DB."""
    if db.execute('PRAGMA user_version').fetchone()[0]!=1:raise ContractError('migration_source_version')
    db.execute('BEGIN IMMEDIATE')
    try:
        db.execute('CREATE TABLE migration_probe(id INTEGER PRIMARY KEY, note TEXT NOT NULL)')
        if fail:raise ContractError('injected_migration_failure')
        db.execute('INSERT INTO migration_probe VALUES(1,?)',('version2 fixture',))
        db.execute('PRAGMA user_version=2')
        db.execute('INSERT INTO schema_migrations VALUES(2,?)',('reference-migration',))
        db.execute('COMMIT')
    except Exception:
        db.execute('ROLLBACK');raise
