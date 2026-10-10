Inventory/combat storage change, 2026-10-09

CX-042 / CX-050 require explicit equipped, module_installed, consumed and sold ownership states. The frozen production schema 001 did not allow them. Original schema files and reference sources remain unchanged.

Migration 002 reconstructs only the items table in an independently backed-up candidate and adds those states while retaining primary keys, slot uniqueness and branch foreign keys. It records user_version 2. Validation and deletion-job replay precede the recovery pointer switch. An exception before promotion preserves the active original and retains the candidate for diagnosis.

Existing baseline storage callers retain schema 1 until PartyService explicitly requests EnsureInventorySchemaAsync. New inventory calls use schema 2. Snapshots/checkpoints/backup registration record the actual version; readers accept versions 1 and 2, refuse higher versions, and retain original backup bytes. No other profile, learning or privacy-control schema changes are planned.

Callers: ProfileStore, PartyService initialization, Godot startup and inventory/combat integration tests. Required validation: schema-1 backup promotion, original SHA preservation, interruption before pointer promotion, schema-2 backups/checkpoints, foreign keys and unique equipment ownership, resource rollback and combat continuation.

Additional concurrency protection: queued operations acquire one OS named mutex per canonical profile path before checking the current control pointer. Retired store objects refuse all further operations. Migration/recovery promotion also uses a compare-and-swap on the expected old pointer. This prevents an old writer from committing to the preserved source while another instance promotes a candidate. Process abandonment releases the OS mutex; SQLite journals retain transaction recovery responsibility.
