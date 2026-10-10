# Twelve independent optional safety contracts

`OPTIONAL-ENCOUNTERS-20261010-v1` fills the twelve unassigned optional rows in the frozen Combat_System appendix, lines 527–538. The appendix supplied the encounter IDs and arena scenes. All detailed targets, keepers, safe stands, English/Chinese notices and numerical enemy configurations in this package are newly authored candidates. Original files and mainline rewards are unchanged. Language target remains 6.5; language, balance, human play and rights review are pending. These twelve reading notices have no audio claim.

The learner package contains 12 contracts, 12 full bilingual reading materials and 36 paragraph pairs. Each has a distinct target, exact inspection action and finite threat configuration. Safe new stands extend the original six arenas without changing their ports, walls or hazard declarations. No new scene is counted. The author-only cards and provenance must remain outside the player export.

## Frozen interfaces

Core: `OptionalContractPackage.Parse(json)`, `OptionalContractRules.Validate(package)`, `OptionalContractRules.ExtendGeometry(package, original)`. The extension only adds the twelve versioned safe spots and checks a safe path to an existing work stand. It does not grant access through a closed original port.

Application constructor: `OptionalEncounterService(IAdventureRepository repository, IEventLeaseStore leases, RuntimeCatalog catalog, OptionalContractPackage package, ImmutableDictionary<string, SceneGeometry> geometry)`.

Commands: `DiscoverAsync(materialId)`, `AcceptContractAsync(encounterId)`, `BeginAsync(encounterId, difficulty="standard", string[]? supplies=null)`, `PreviewActionAsync(encounterId, BattleRequest)`, `ConfirmAsync(token)`, `ClosePreview(token)`, `WithdrawAsync(encounterId)`, `RevokeContractAsync(encounterId)`, `RetryAsync(encounterId, difficulty=null)`, `InspectTargetAsync(encounterId, exactInspectionAction)`. Reads: `MaterialsAsync`, `OpenMaterialAsync`, `PermitAsync`, `EncounterAsync`, `ResultAsync`.

Source entity `optional-encounter:source:<material>` records current source version and displayed English-body SHA256. Permission entity `optional-encounter:permit:<ENC>` records the contract instance, exact target/purpose/source/actual regional entry receipt. `optional_contract_accepted` is the permission receipt. It authorises only this notice, these threats and this target. Primary quest permission alone never accepts an additional contract. A prior same-instance result prevents respawning.

Battle entity `combat:<ENC>` uses the shared BattleEngine, actual PartyService level/grade/equipment/modules, and current RosterService lawful helpers. Enemy HP scales through the existing declared difficulty rule; enemy names, HP, armour, threshold and alternating intent are public in each notice. No enemy or action grants experience. Continue reads the same snapshot. Active/defeated entry withdrawal restores all carried personal items and exact actor/entry state; retry retains battle and contract identities while issuing a new attempt. Permission withdrawal restores an active entry atomically and invalidates old previews.

Threat control alone has no accepted result. The subsequent exact onsite inspection commits `optional_contract_result` with semantic key `optional-contract:<ENC>:result` and entity `optional-encounter:result:<ENC>`. Typed `OptionalContractResult` binds current contract/battle/attempt/source fingerprint/target/inspection/permission receipt/real regional receipt. It always contains Experience=0, Marks=0, Parts=0. Result reads verify the actual controlled battle and matching source receipts. No tower readiness, quest closure, custody, recruitment, private material, delivery or ending predicate is changed.

R1 requires actual current Q104 site acceptance. R2–R6 require the matching production region-initialization receipt/version. R3 also requires the actual inspected SideWellVisit and paired arrival, retaining the SideWell producer's original safety contract. R6 additionally requires the protected current base cargo to be actually received; unhanded transport blocks additional work. Final preparation sealing pauses these contracts. No fixture authorisation is used by positive tests.

## Root wiring (owned by Root)

All five C# files were completed as `.cs.pending`; Root has since integrated their `.cs` forms for serial validation:

- `src/Mist.Core/OptionalEncounterContracts/OptionalEncounterModels.cs.pending`
- `src/Mist.Application/OptionalEncounterContracts/OptionalEncounterService.cs.pending`
- `tests/Mist.Tests/OptionalEncounterContracts/OptionalEncounterCases.cs.pending`
- `game/UI/Shell/Root.OptionalEncounterContracts.cs.pending`
- `game/Diagnostics/Root.OptionalEncounterClientTests.cs.pending`

Call `LoadOptionalEncounterContracts(MergeRegionalGeometry(...))` once the current store/catalog and the complete source geometry are loaded. Include `ExtendOptionalEncounterGeometry(...)` after the original regional geometry merge. Call `ResetOptionalEncounterUi` with transient resets. `OptionalEncounterSelected` restores an active/defeated selection from the current persisted snapshot.

Dispatch `TryOptionalEncounterInteraction(spot)` before the ordinary regional handlers; it accepts only the twelve new IDs. It does not intercept any original mainline spot. Append `AddOptionalEncounterArchive(flow)` to acquired material views. The loader registers current learning source access through `RegisterRegionalLearningScope`; opening a source never grants another material's context.

In shared encounter dispatch, place the optional discriminator before Field/First/region/workshop branches and use `OptionalCurrentEncounterAsync`, `OptionalPreviewEncounter`, `OptionalConfirmEncounter`, `OptionalRetryEncounter`, `OptionalWithdrawEncounter`, `OptionalCloseEncounterPreview`. Shared battle title is `OptionalEncounterTitle`. The battle's pause link should open the selected contract via `OpenOptionalEncounterMaterial` instead of opening a main quest draft. Controlled guidance should direct the player back to the same stand for the separate target inspection. This adapter exposes difficulty and optional real supply selection, including empty default selection.

Compiler copies only `learner_package.json` to `game/content/optional_encounter_contracts/learner_package.json`; validate through the exact Core parser. Never copy author-only generators/cards/provenance. The main registry hook is `Mist.Tests.OptionalEncounterContracts.OptionalEncounterCases.Register()` (8 cases: 2 contract/negative checks and 6 actual regional routes each controlling both independent encounters). No build or C# test was executed by this agent.

## Actual native sources and drivers

Each regional production case exports one actual untouched copy *before either optional source/permission/battle/result exists*. `normal-optional-r1-native-entry` through `normal-optional-r6-native-entry` each contain their uniquely named `normal-optional-rN-native-entry.json`. These are actual SQLite backups plus matching deletion/control and managed archive files. XP is inherited through normal production, never assigned by a fixture. Each region's two contracts share the true entry route; the test controls twelve total encounters.

The R4 scoped run at reports/build/20261010T075015234237Z exposed an export failure: the actual inventory-v2 world lived at recovery/inventory-v2-f327f894-ffcd-4375-aa71-3e13a73975bc.sqlite, while the old export placed the online backup only at profile.sqlite. The unchanged control pointer correctly refused that missing target. ExportEntry now puts the real consistent online backup at the same relative active-generation path, preserves the original control pointer and copies its recovery/checkpoint/backup archives. Its read-back asserts the same generation and exact source BranchSnapshot, with no source edits, fixture entry or control-pointer rewrite. The earlier failure is retained; this source fix awaits Root's actual rerun.

Register six flags `--optional-r1-client-test` … `--optional-r6-client-test`, each calling `RunOptionalEncounterClientTests()`. The driver reads both complete originals in the real UI, checks 200% layout, accepts both independent permits, rejects a wrong actual work stand, controls all shared battle choices, previews/cancels, Continues without healing, withdraws the full entry, revokes/reaccepts, retries the same instance, and performs the separate exact target inspection through the real button. Report `optional_rN_client_results.json`. This is automated native engineering evidence; human play, physical IME, screen reader, numeric balance and language review remain not_run/pending.

The R1 route purchases one actual CON-04 in a lawful original safe area before the entry export. The scoped production case consumes it through the actual combat SUPPLY command, proves the item became consumed, and then proves full withdrawal returns it to player ownership. The final two contract results add no item or reward. Default native entry selection leaves carried items unchecked.
