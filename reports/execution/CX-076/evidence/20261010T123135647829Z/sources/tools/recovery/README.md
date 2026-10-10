# CX-076 recovery engineering candidate

`AuditRecoverySource.ps1` reads an explicitly selected local input and compares its actual SHA-256 with a declared checksum. It never migrates, promotes, uploads, repairs or deletes that file. Exit 0 means the bytes match; exit 3 means mismatch; exit 2 means the source could not be read. A matching checksum does not prove a trusted signature, database identity, schema compatibility or permission to replace a live profile. The storage migration launcher has not been bound to this preflight.

```powershell
powershell.exe -NoProfile -NonInteractive -File .\tools\recovery\AuditRecoverySource.ps1 -LiteralPath '<explicit local candidate file>' -ExpectedSha256 '<declared 64-character SHA-256>'
```

`Mist.Tests.WindowsRecovery.WindowsRecoveryCases.Register()` adds 14 isolated engineering cases to the existing C# runner. Existing `--storage-child` / `StorageCases.Child(string[] args)` is reused; no new child dispatch is needed. Only the parent verification process should register and run these cases. Test files are created in unique `windows-recovery-*` subdirectories below `MISTSEA_TEST_OUTPUT`, or the temporary MistSeaTests directory when unspecified.

Actual fault operations, when the cases run, include current-user file DACL denial, native SQLite `SQLITE_FULL` under a connection page quota, a discarded response after actual commit, corrupted source bytes, committed or interrupted migration copies, moved deletion-control files, old backups, corrupt originals, and before/after-commit operating-system child process termination. The DACL operation adds only WriteData/AppendData denial to the newly created fixture database file and restores its original access section in `finally`. It never changes a directory ACL, the current user's real profile or any global ACL.

The capacity case creates and removes `recovery_capacity_engineering_fixture`, uses native SQL to exercise an aborted transaction against the fixture's draft/wallet rows, and attempts an 8 MiB zeroblob under `max_page_count`. It is explicitly a SQLite quota fault, not a physically full system drive or an authored world task. It does not claim that the native editor has handled the failure. Process termination is explicitly not physical power loss.

Evidence files are written by the cases only after actual operations and assertions; failed or unavailable ACL setup is marked in attempt evidence. All evidence retains `release_gate_accepted=false` and no human signoff. Files named `migration-*.sqlite` remain in the isolated recovery directories for examination; invalid candidates are never promoted. Corrupt-main recovery records the selected backup sequence and the missing newer draft/world/observation range: restoring a full database backup does not guarantee zero loss or behave like a world-only checkpoint.

Still `not_run`: physical power loss, physical system-volume exhaustion, another machine, installed formal distribution, native failure/export/retry UX and IME preedit recovery. A current-executable schema/content-binding rejection is not a run of an older distributed executable. Logical deletion replay is not SSD secure erasure or destruction of every historic backup. This candidate does not accept CX-076 or its prerequisite release gates by itself.
