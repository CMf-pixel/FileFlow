# Milestone 2: reliable rule storage

**Goal:** Implement only the user's approved persistence requirements in the existing Core project, using System.Text.Json and isolated filesystem tests.

**Baseline:** `codex/milestone-2-persistence` was fast-forwarded to remote main `89439b0`. Milestone 1 is present and the tree was clean before edits. The baseline test runner was blocked by Windows application control (0x800711C7); a zero exit code with no discovered tests is not a passing run.

**Design:** `FileRuleStore` accepts an explicit storage directory for tests and defaults to LocalApplicationData/FileFlow. `Load` and `Save` return `RuleStoreResult`, with either rules or a typed persistence error. JSON contains `schemaVersion: 1` and an ordered `rules` array with all six required fields. Structural validation never requires directories to exist and persistence preserves the supplied field values.

Save validates existing data before touching it. It writes a unique temporary file in the storage directory, flushes it to disk, stages and flushes an exact copy of the current valid file as `rules.json.bak`, then publishes the new active file using a same-directory overwrite rename. It never deletes the active file first. Publishing a backup separately avoids the documented partial-failure states of Windows ReplaceFile. A persistent, exclusively opened `rules.json.lock` prevents overlapping cooperating writers. Loads never restore backups or promote temporary files. Failed cleanup leaves inert temporary files; cleanup never changes the active file.

## Steps

- [x] Verify remote main, Milestone 1 sources, and clean working tree.
- [x] Add tests in `tests/FileFlow.Core.Tests/FileRuleStoreTests.cs`: both actions and all fields, order, first launch, explicit schema fixtures, malformed/unsupported data, missing fields, vanished directories, backup rotation, failed publication, blocked backup, blocked storage, competing writer, and inert leftover files. All writes use a unique directory beneath the OS temporary directory.
- [x] Attempt the tests before implementation; distinguish missing behavior from environment failures.
- [x] Implement `src/FileFlow.Core/Persistence/FileRuleStore.cs`, `RuleStoreResult.cs`, and `RulePersistenceError.cs`. Keep all filesystem behavior here, with no UI, packages, projects, or execution features.
- [x] Update README with JSON format, API, failure semantics, and recovery limits.
- [x] Review the implementation against all eleven requested test cases and failure ordering.
- [x] Run filtered Milestone 2 tests, full suite, and `dotnet build FileFlow.sln --configuration Release`. Report actual executed test counts and any environment blockers.

No changes to main, automatic merge, or later milestones. Work is executed in the user's existing milestone branch.

## Verification outcome

Final filtered run: 51 passed, zero failed/skipped. Full suite: 130 passed (79 existing + 51 new), zero failed/skipped. Release solution build succeeded with zero errors and one NU1900 warning because the NuGet vulnerability feed was unreachable. Commands used `--no-restore` with the already-restored packages. The initial application-control block did not recur in final runs; no security policy was changed.

Review found ambiguous enum-string parsing and blocked parent paths being interpreted as first launch. Four regression cases reproduced these failures before the fixes. Strict action checks and a read-only ancestor check resolve them. Follow-up review found no remaining concerns. The failed-publication test also confirms that an existing backup can advance to the still-active valid document before active publication fails.

No scope deviations. Changes remain uncommitted on `codex/milestone-2-persistence`; main is unchanged.
