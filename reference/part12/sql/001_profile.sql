PRAGMA foreign_keys=ON;
PRAGMA journal_mode=DELETE;
PRAGMA synchronous=FULL;
PRAGMA busy_timeout=5000;
CREATE TABLE schema_migrations(version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);
INSERT INTO schema_migrations VALUES(1,'reference-baseline');
CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE branches(
 branch_id TEXT PRIMARY KEY, parent_branch_id TEXT, parent_checkpoint_id TEXT,
 release_id TEXT NOT NULL, sequence INTEGER NOT NULL DEFAULT 0 CHECK(sequence>=0));
CREATE TABLE entities(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), entity_id TEXT NOT NULL,
 kind TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>=0), payload_json TEXT NOT NULL CHECK(json_valid(payload_json)),
 PRIMARY KEY(branch_id,entity_id));
CREATE TABLE items(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), item_id TEXT NOT NULL, definition_id TEXT NOT NULL,
 owner_kind TEXT NOT NULL CHECK(owner_kind IN('player','vessel','warehouse','installed','reward_box')),
 owner_id TEXT NOT NULL, slot TEXT,
 PRIMARY KEY(branch_id,item_id), UNIQUE(branch_id,owner_id,slot));
CREATE TABLE balances(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), account TEXT NOT NULL,
 quantity INTEGER NOT NULL CHECK(quantity>=0), PRIMARY KEY(branch_id,account));
CREATE TABLE receipts(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), semantic_key TEXT NOT NULL,
 receipt_id TEXT NOT NULL, action_type TEXT NOT NULL, effect_hash TEXT NOT NULL,
 result_json TEXT NOT NULL CHECK(json_valid(result_json)),
 PRIMARY KEY(branch_id,semantic_key), UNIQUE(branch_id,receipt_id));
CREATE TABLE command_log(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), command_id TEXT NOT NULL,
 request_hash TEXT NOT NULL, receipt_id TEXT NOT NULL,
 PRIMARY KEY(branch_id,command_id), FOREIGN KEY(branch_id,receipt_id) REFERENCES receipts(branch_id,receipt_id));
CREATE TABLE outbox(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), event_id TEXT NOT NULL,
 receipt_id TEXT NOT NULL, event_type TEXT NOT NULL,
 payload_json TEXT NOT NULL CHECK(json_valid(payload_json)), displayed INTEGER NOT NULL DEFAULT 0 CHECK(displayed IN(0,1)),
 PRIMARY KEY(branch_id,event_id), FOREIGN KEY(branch_id,receipt_id) REFERENCES receipts(branch_id,receipt_id));
CREATE TABLE draft_revisions(
 draft_id TEXT NOT NULL, revision INTEGER NOT NULL CHECK(revision>0), source_branch_id TEXT NOT NULL,
 material_version TEXT NOT NULL, body TEXT, body_hash TEXT NOT NULL,
 deleted INTEGER NOT NULL DEFAULT 0 CHECK(deleted IN(0,1)), PRIMARY KEY(draft_id,revision));
CREATE TABLE draft_heads(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), task_id TEXT NOT NULL,
 draft_id TEXT NOT NULL, revision INTEGER NOT NULL,
 PRIMARY KEY(branch_id,task_id), FOREIGN KEY(draft_id,revision) REFERENCES draft_revisions(draft_id,revision));
CREATE TABLE observations(
 observation_id TEXT PRIMARY KEY, profile_epoch INTEGER NOT NULL, source_branch_id TEXT NOT NULL,
 family_id TEXT NOT NULL, attempt_id TEXT NOT NULL, support_state TEXT NOT NULL,
 payload_json TEXT NOT NULL CHECK(json_valid(payload_json)), UNIQUE(profile_epoch,attempt_id));
CREATE TABLE ai_requests(
 request_id TEXT PRIMARY KEY, profile_epoch INTEGER NOT NULL, consent_version INTEGER NOT NULL,
 branch_id TEXT NOT NULL, load_context TEXT NOT NULL, draft_id TEXT NOT NULL, draft_revision INTEGER NOT NULL,
 status TEXT NOT NULL CHECK(status IN('pending','current','old_draft','archived_branch','discarded')),
 response_json TEXT CHECK(response_json IS NULL OR json_valid(response_json)));
CREATE TABLE checkpoints(
 checkpoint_id TEXT PRIMARY KEY, source_branch_id TEXT NOT NULL, release_id TEXT NOT NULL,
 snapshot_schema INTEGER NOT NULL, sequence INTEGER NOT NULL,
 snapshot_json TEXT NOT NULL CHECK(json_valid(snapshot_json)), checksum TEXT NOT NULL);
CREATE TABLE save_slots(slot_id TEXT PRIMARY KEY, category TEXT NOT NULL, checkpoint_id TEXT NOT NULL REFERENCES checkpoints(checkpoint_id));
CREATE TABLE leases(branch_id TEXT NOT NULL, resource_id TEXT NOT NULL, event_id TEXT NOT NULL,
 load_context TEXT NOT NULL, PRIMARY KEY(branch_id,resource_id));
CREATE TABLE settings(key TEXT PRIMARY KEY,value_json TEXT NOT NULL CHECK(json_valid(value_json)));
PRAGMA user_version=1;
