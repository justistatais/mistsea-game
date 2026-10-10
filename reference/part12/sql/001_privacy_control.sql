PRAGMA journal_mode=DELETE;
PRAGMA synchronous=FULL;
CREATE TABLE IF NOT EXISTS profile_controls(
 profile_id TEXT PRIMARY KEY, learning_epoch INTEGER NOT NULL CHECK(learning_epoch>=0),
 consent_version INTEGER NOT NULL CHECK(consent_version>=0));
CREATE TABLE IF NOT EXISTS erasure_jobs(
 job_id TEXT PRIMARY KEY, profile_id TEXT NOT NULL, scope TEXT NOT NULL,
 object_id TEXT, epoch INTEGER NOT NULL, status TEXT NOT NULL CHECK(status IN('pending','applied')));
-- This store is not loaded from an adventure checkpoint or ordinary profile backup.
PRAGMA user_version=1;
