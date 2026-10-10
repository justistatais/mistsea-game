"""Isolated bundled-native SQLite diagnostic; never opens an existing game profile."""
from pathlib import Path
import ctypes, datetime, hashlib, json, uuid

root=Path(__file__).resolve().parents[2]
dll=Path(r"C:/Users/20594/AppData/Local/MistSeaToolchain/nuget/packages/sqlitepclraw.lib.e_sqlite3/2.1.12/runtimes/win-x64/native/e_sqlite3.dll")
native=ctypes.CDLL(str(dll))
pointer=ctypes.c_void_p
native.sqlite3_open_v2.argtypes=[ctypes.c_char_p,ctypes.POINTER(pointer),ctypes.c_int,ctypes.c_char_p]
native.sqlite3_open_v2.restype=ctypes.c_int
native.sqlite3_close.argtypes=[pointer]
native.sqlite3_close.restype=ctypes.c_int
native.sqlite3_errmsg.argtypes=[pointer]
native.sqlite3_errmsg.restype=ctypes.c_char_p
native.sqlite3_exec.argtypes=[pointer,ctypes.c_char_p,pointer,pointer,ctypes.POINTER(ctypes.c_char_p)]
native.sqlite3_exec.restype=ctypes.c_int
native.sqlite3_free.argtypes=[pointer]
native.sqlite3_libversion.restype=ctypes.c_char_p
native.sqlite3_backup_init.argtypes=[pointer,ctypes.c_char_p,pointer,ctypes.c_char_p]
native.sqlite3_backup_init.restype=pointer
native.sqlite3_backup_step.argtypes=[pointer,ctypes.c_int]
native.sqlite3_backup_step.restype=ctypes.c_int
native.sqlite3_backup_finish.argtypes=[pointer]
native.sqlite3_backup_finish.restype=ctypes.c_int
native.sqlite3_prepare_v2.argtypes=[pointer,ctypes.c_char_p,ctypes.c_int,ctypes.POINTER(pointer),ctypes.POINTER(ctypes.c_char_p)]
native.sqlite3_prepare_v2.restype=ctypes.c_int
native.sqlite3_step.argtypes=[pointer]
native.sqlite3_step.restype=ctypes.c_int
native.sqlite3_column_int.argtypes=[pointer,ctypes.c_int]
native.sqlite3_column_int.restype=ctypes.c_int
native.sqlite3_finalize.argtypes=[pointer]

def extend(path):
    full=str(path.absolute())
    if full.startswith("\\\\?\\"):return full
    return "\\\\?\\UNC\\"+full[2:] if full.startswith("\\\\") else "\\\\?\\"+full

def open_db(path,vfs,extended=False,read_only=False):
    database=pointer()
    actual=extend(path) if extended else str(path.resolve())
    result=native.sqlite3_open_v2(actual.encode("utf-8"),ctypes.byref(database),1 if read_only else 6,vfs.encode() if vfs else None)
    return database,dict(open_rc=result,error=native.sqlite3_errmsg(database).decode("utf-8"),data_source=actual)

def execute(db,sql):
    error=ctypes.c_char_p()
    rc=native.sqlite3_exec(db,sql.encode(),None,None,ctypes.byref(error))
    detail=error.value.decode("utf-8") if error.value else ""
    if error.value:native.sqlite3_free(error)
    return dict(rc=rc,error=detail)

def count(db):
    statement=pointer()
    rc=native.sqlite3_prepare_v2(db,b"SELECT count(*) FROM payload",-1,ctypes.byref(statement),None)
    try:return native.sqlite3_column_int(statement,0) if rc==0 and native.sqlite3_step(statement)==100 else None
    finally:
        if statement:native.sqlite3_finalize(statement)

work=root/"reports/diagnostics"/("sqlite-longpath-"+uuid.uuid4().hex)
work.mkdir(parents=True)
deep=work
while len(str(deep.resolve()))<300:deep=deep/"Unicode资料 and rollback journal"
Path(extend(deep)).mkdir(parents=True)
records=[]
for name,path,vfs,extended in [
    ("short-default",work/"short.sqlite",None,False),
    ("deep-default",deep/"default.sqlite",None,False),
    ("deep-longpath-vfs",deep/"longpath.sqlite","win32-longpath",False),
    ("deep-extended-default",deep/"extended-default.sqlite",None,True),
    ("deep-extended-longpath-vfs",deep/"extended-longpath.sqlite","win32-longpath",True),
]:
    db,result=open_db(path,vfs,extended)
    record=dict(name=name,vfs=vfs,extended=extended,logical_path=str(path.resolve()),logical_path_chars=len(str(path.resolve())),**result)
    try:
        if result["open_rc"]==0:
            record["setup"]=execute(db,"PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; CREATE TABLE payload(value TEXT NOT NULL);")
            record["actual_write"]=execute(db,"BEGIN IMMEDIATE; INSERT INTO payload VALUES('native path diagnostic');")
            journal=Path(str(path)+"-journal")
            physical_journal=Path(extend(journal))
            record["during_transaction_journal"]=dict(logical_path=str(journal.resolve()),chars=len(str(journal.resolve())),exists=physical_journal.exists(),bytes=physical_journal.stat().st_size if physical_journal.exists() else None)
            record["commit"]=execute(db,"COMMIT;")
            record["stored_rows"]=count(db)
            if name=="deep-extended-longpath-vfs" and record["stored_rows"]==1:
                target_path=deep/"actual-backup.sqlite"
                target,target_result=open_db(target_path,vfs,True)
                record["backup"]=dict(**target_result,logical_path_chars=len(str(target_path.resolve())))
                try:
                    if target_result["open_rc"]==0:
                        backup=native.sqlite3_backup_init(target,b"main",db,b"main")
                        if backup:
                            record["backup"]["step_rc"]=native.sqlite3_backup_step(backup,-1)
                            record["backup"]["finish_rc"]=native.sqlite3_backup_finish(backup)
                            record["backup"]["stored_rows"]=count(target)
                        else:record["backup"]["init_error"]=native.sqlite3_errmsg(target).decode()
                finally:native.sqlite3_close(target)
    finally:native.sqlite3_close(db)
    if result["open_rc"]==0:
        reopened,reopen_result=open_db(path,vfs,extended,True)
        try:record["reopen"]=dict(**reopen_result,stored_rows=count(reopened) if reopen_result["open_rc"]==0 else None)
        finally:native.sqlite3_close(reopened)
    records.append(record)

report=dict(scope="isolated actual bundled SQLite native path and rollback-journal diagnostic, not game/test suite/human acceptance",generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),dll=str(dll),dll_sha256=hashlib.sha256(dll.read_bytes()).hexdigest(),sqlite_version=native.sqlite3_libversion().decode(),artifact_root=str(work),cases=records,unc_actual_not_run=True,game_or_tests_compilation_run=False)
out=root/"reports/continuous/sqlite_windows_path_probe.actual.json"
out.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(dict(output=str(out),sqlite=report["sqlite_version"],cases=[dict(name=r["name"],open_rc=r["open_rc"],write_rc=r.get("actual_write",{}).get("rc"),journal=r.get("during_transaction_journal"),rows=r.get("stored_rows"),backup=r.get("backup")) for r in records]),ensure_ascii=False))
