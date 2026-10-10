# R3-O01 actual owner-local music-box work

Status: five original C# files are now `.cs` and MusicBoxRepairCases is registered in Program; a sixth DEBUG native driver remains `.cs.pending` for Root include. No build, test run or native-playability claim was made by this agent.

## Include and register

- Included `src/Mist.Core/OptionalLocalWorks/MusicBoxModels.cs`.
- Included `src/Mist.Application/OptionalLocalWorks/MusicBoxRepairService.cs` and `MusicBoxRepairService.Note.cs`.
- Included `tests/Mist.Tests/OptionalLocalWorks/MusicBoxRepairCases.cs`; `Mist.Tests.OptionalLocalWorks.MusicBoxRepairCases.Register()` registered in Program. Four service cases use task filter `CP-OPTIONAL-MUSICBOX`; one normal native-entry exporter uses `CP-OPTIONAL-MUSICBOX-05`.
- Included `game/UI/Shell/Root.OptionalLocalWorks.cs`.
- Root to rename `game/Diagnostics/Root.MusicBoxClientTests.cs.pending`, guarded by `#if DEBUG`.

## Native driver / build.py hookup pending Root

`CP-OPTIONAL-MUSICBOX-05` invokes public `ChapterThreeCases.ActualArrival(ProfileStore)` from an unseeded new profile, checks 1920 XP/actual R3 landing/no local acquisition/no optional objects/no positive fixture, persists a manual checkpoint and emits exact `musicbox-normal-native-entry-*/normal-musicbox-native-entry.json` beside the actual SQLite profile. The file records `profile_root` and real receipts; it is provenance, not a gameplay fixture.

Root's build.py should execute that task, locate the proof and copy its actual profile directory into a fresh native-test directory; set `MISTSEA_PROFILE_ROOT` to that copy and `MISTSEA_CLIENT_TEST_OUTPUT` to the native output. Route `--musicbox-client-test` after the normal campaign handoff shell initializes to `await RunMusicBoxClientTests()`; include that flag in the boot-error exit list. No internal-Q102 flag or startup completion seed is used.

The DEBUG controller operates existing `InteractSelected` and actual station/reader/part/repair/editor/meaning/preview buttons, with actual safe navigation commands. It checks unselected choices, full acquired private specification, rejection of the fourteen-tooth wheel, actual CAS acknowledgement, save-only noncompletion, read-only preview, ClosePanel cancellation of the actual token, one private same-box receipt, no repeat delivery button and invariant main/balance/six-tower state. It emits `musicbox_client_results.json` plus `MISTSEA_MUSICBOX_CLIENT_TESTS`, exiting nonzero on a failed check. It has not been executed here; human play, physical IME and language review remain pending. Root owns shared dispatcher/close/save/load/startup hooks and build.py wiring.

The service constructor is `(IAdventureRepository, RuntimeCatalog, ChapterThreePackage)`. Existing R3 scene geometry/material/writing task are read, never rewritten. Actual navigation remains `ChapterThreeService.TraverseAsync/EnterPortAsync`; R3-S02↔R3-O01 already opens only after real landing registration. This module adds no scene or route and does not alter the frozen main service's pending handler.

## Shared Root hooks (Root owns these edits)

1. Once `_chapterThree` has loaded its real package, call `InitializeOptionalLocalWorks()`; lazy initialization also occurs on the first shop interaction.
2. In the interaction dispatcher, call `await TryOptionalLocalWorksInteraction(spot)` **before** the existing `TryChapterThreeInteraction(spot)`. The new handler intercepts all four R3-O01 spots, so the old `propose_musicbox_repair` pending response and inaccessible old material reader are never used for this work. R3-O02 stays explicitly pending.
3. In `ClosePanel(title)`, call `CloseOptionalLocalWorksUiPreview(title)` so cancellation closes the service token. No side effect occurs from closing a preview.
4. Before manual slot/Continue/backup/quit, await `SaveOptionalLocalWorksDraft()` alongside the other draft writers. If false, preserve the current editor and await IME completion or show the existing save-failure path. No Ctrl+Enter binding is added.
5. On successful profile load/Continue/branch replacement, call `ResetOptionalLocalWorksUiState()` before reinitialization. Stale service commands also reject Profile/Branch/Load/Release mismatch before taking a foreground lease.
6. Archive navigation can add a music-box button only when `await HasOptionalLocalWorksArchive()` is true; it opens `OpenOptionalLocalWorksArchive()`. The source remains stored but becomes unreadable after permission withdrawal. Existing broad R3 archive intentionally cannot grant this private material.

All new `_musicBox*` fields are declared in this partial; Root should not redeclare them. There is no new geometry-selection hook or automatic background acquisition.

## Physical sequence and public service API

- `DecideInvitationAsync("accept"|"defer"|"decline"|"withdraw")`: `R3-O01.owner` (26,35). On first acceptance, registers the fixed local owner's one box and two finite shop wheels under existing `warehouse` custody. It does not grant them to the player. Repair/read permission covers the same mechanism only; memory publication is false.
- `DiscoverSpecificationAsync()` / `OpenSpecificationAsync()`: acquire at `R3-O01.parts` (74,35), then permission-aware read. Uses the existing bilingual `R3-O01-R-MUSIC` unchanged.
- `InspectMechanismAsync()`: `R3-O01.musicbox` (26,65), actual same-box custody and acquired specification.
- `SelectWheelAsync(definition,teeth)`: parts. Only the source's twelve-tooth MBOX-12 is compatible; fourteen-tooth MBOX-14 is rejected and left in its own store.
- `RequestOwnerApprovalAsync()`: owner. Emits `musicbox_owner_part_approved` typed `MusicBoxApproval`, binding current permission and actual serial.
- `IsolateMechanismAsync()`, `FitApprovedWheelAsync()`, `CheckTurningAsync()`: musicbox. Stop winding before fitting; install the actual wheel in the actual box; verify the current configuration and emit typed `MusicBoxTurningCheck`. A configuration or permission change invalidates the old test.
- `ReturnSameBoxAsync()`: owner. Moves the same serial back from the owner's local bench to the owner, leaving the wheel installed in that box.
- After withdrawal, `ReturnPausedBoxAsync()` safely returns that same box without settling repair; historical installation/source records are retained. Reacceptance plus new approval permits `ResumeSameBoxAsync()` at the owner, returning that same serial to the bench for a new current check without producing more stock.
- `SaveDraftAsync(expectedRevision,draftId,body)` saves actual CAS to existing `R3-O01-W-REPAIRNOTE`; it performs no world work. `ConfirmNoteMeaningAsync(fields,structuredAssistance)` is at `R3-O01.note` (74,65), binds that actual nonempty draft hash and explicitly makes no automatic English-understanding/score claim.
- `PreviewDeliveryAsync()` is read-only and at the owner. It checks source/permission/current physical part/current test/same box returned/current saved draft/current interpretation. `ConfirmPreviewAsync(token)` atomically receives the note once at fixed semantic `R3-O01:musicbox-repair`, action `musicbox_repair_note_received`, entity `R3-O01:musicbox-work:delivery`, typed `MusicBoxRepairDelivery`. `ClosePreview(token)` cancels.

Meaning fields: `part=MBOX-12`, `check=turning_mechanism_only`, `box=R3-O01:MUSIC-BOX-01`, `return=same_owner`, `limits=mechanism_only`, `memory=private_not_published`. These are explicit interpretations, not inferred English answers; the UI provides an unselected choice for every field.

## World limits and verification scope

There is no registered reward budget for this optional candidate: 0 XP/0 marks/0 parts, no main-quest closure, tower readiness, six-tower stock, companion recruitment, policy change or ending entitlement. Local custody uses only frozen schema kinds `warehouse` and `installed`. Foreground combat/event/cargo and final-departure guards apply to every world command.

The four written SQLite cases exercise actual zero-XP prior chapter chain and normal R3 arrival, wrong stand/14-tooth/missing approvals/isolation/test, same-box refusal against a clearly negative-only custody probe, withdrawal and actual pause/resume, private-memory rejection, read-only/cancelled previews, real draft CAS, Continue generation, exactly-once delivery and main-world invariance. Root must actually run them before claiming passed. No native input driving or human language calibration is claimed.

## Source and unresolved branches

See `content/source/optional_local_works/MUSICBOX_AUTHOR_CARD.md` for original/candidate separation. This implements the existing learner candidate's owner-present return-to-owner outcome. The original story seed's old-recording localization, public-rest-room placement and absent-owner keeping outcome remain unimplemented; no audio is generated or falsely counted as listened. R3-O02 controlled comparison/extra sample permit and other NPC/companion pending nodes remain outside this scoped producer.
