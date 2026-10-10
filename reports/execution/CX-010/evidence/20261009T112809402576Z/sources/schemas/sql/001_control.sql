PRAGMA journal_mode=DELETE;
PRAGMA synchronous=FULL;
CREATE TABLE IF NOT EXISTS profile_controls(
 profile_id TEXT PRIMARY KEY, learning_epoch INTEGER NOT NULL CHECK(learning_epoch>=0),
 consent_version INTEGER NOT NULL CHECK(consent_version>=0), external_allowed INTEGER NOT NULL DEFAULT 0 CHECK(external_allowed IN(0,1)));
CREATE TABLE IF NOT EXISTS erasure_jobs(
 job_id TEXT PRIMARY KEY, profile_id TEXT NOT NULL, scope TEXT NOT NULL,
 object_id TEXT, epoch INTEGER NOT NULL, status TEXT NOT NULL CHECK(status IN('pending','applied')));
-- This store is not loaded from an adventure checkpoint or ordinary profile backup.
PRAGMA user_version=1;

CREATE TABLE IF NOT EXISTS backup_registry(backup_id TEXT PRIMARY KEY, relative_path TEXT UNIQUE NOT NULL, sha256 TEXT NOT NULL, sequence INTEGER NOT NULL, schema_version INTEGER NOT NULL, validated INTEGER NOT NULL CHECK(validated IN(0,1)));
CREATE TABLE IF NOT EXISTS recovery_pointers(profile_id TEXT PRIMARY KEY, current_path TEXT NOT NULL, pending_candidate TEXT);
