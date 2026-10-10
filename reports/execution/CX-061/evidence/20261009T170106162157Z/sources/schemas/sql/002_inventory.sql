CREATE TABLE items_v2(
 branch_id TEXT NOT NULL REFERENCES branches(branch_id), item_id TEXT NOT NULL, definition_id TEXT NOT NULL,
 owner_kind TEXT NOT NULL CHECK(owner_kind IN('player','vessel','warehouse','installed','reward_box','equipped','module_installed','consumed','sold')),
 owner_id TEXT NOT NULL, slot TEXT,
 PRIMARY KEY(branch_id,item_id), UNIQUE(branch_id,owner_id,slot));
INSERT INTO items_v2 SELECT branch_id,item_id,definition_id,owner_kind,owner_id,slot FROM items;
DROP TABLE items;
ALTER TABLE items_v2 RENAME TO items;
INSERT INTO schema_migrations VALUES(2,'inventory-lifecycle-2');
PRAGMA user_version=2;
