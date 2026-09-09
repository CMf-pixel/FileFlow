# Milestone 3: synchronous read-only preview

**Goal:** Implement only the approved preview subsystem in Core and its existing test project. No filesystem execution, WPF, new projects, packages, async services, or later milestones.

**Baseline:** The clean `codex/milestone-3-preview` branch was fast-forwarded from `9d5a381` to fetched remote main `b1e8d53`, which contains Milestones 1 and 2. Baseline verification executed 130 passing tests, zero failures/skips; the cached NuGet audit warning NU1900 remained.

**Design:** Synchronous `FileRulePreviewer.CreatePreview(FileRule)` reuses structural validation and extension matching. Immutable output contains a normalized rule snapshot, deterministically ordered proposed operations with source length/UTC last-write time, and typed blocking issues. The internal filesystem adapter exposes only drive type, attributes, direct enumeration, and source metadata.

Status precedence: any incomplete scan/inspection is `Incomplete`; otherwise issues produce `Blocked`, zero operations produce `NoMatches`, and operations without issues produce `Ready`. Confirmed validation/preflight rejection is blocked. Retain known operations on incomplete scans. `CanExecute` is eligibility only; no execution capability exists.

Check local paths and directories before enumeration. Reject reparse directory components from root down and direct reparse entries without traversal or tag parsing. Enumerate only direct entries, preserve filenames, capture metadata, and block destination occupants and equivalent planned destination names. Expected inspection/enumeration failures become issues. This observation is not an atomic snapshot or execution-time guarantee.

## Implementation checklist

- [x] Verify clean branch, fetch main, fast-forward, confirm both prior milestones, and run the 130-test baseline.
- [x] Add preview tests first and observe compilation failure for the missing preview API.
- [x] Implement immutable models, synchronous engine, and inspection-only adapter.
- [x] Verify initial 46 preview tests, including real directory/content preservation and injected failure/status precedence cases.
- [x] Document the API, status precedence, non-recursion, unsupported paths/reparse entries, and snapshot limitations in README.
- [x] Complete independent read-only review and address actionable findings.
- [x] Run final preview tests, full suite, and Release solution build; record actual results and environment limitations.

Keep changes on the milestone branch. Do not merge, push, create a PR, or begin Milestone 4.

## Verification outcome

- `dotnet test tests/FileFlow.Core.Tests/FileFlow.Core.Tests.csproj --no-restore --filter FullyQualifiedName~Preview`: 49 passed, 0 failed, 0 skipped.
- `dotnet test FileFlow.sln --no-restore`: 179 passed, 0 failed, 0 skipped (130 baseline plus 49 preview tests).
- `dotnet build FileFlow.sln --configuration Release --no-restore`: succeeded, 0 errors, 1 NU1900 warning because the NuGet vulnerability feed was unreachable. Existing restored packages were used; no dependencies changed.
- Independent read-only review found no blocking defects. Added three coverage cases for missing destination parent versus missing final name and unreadable nonmatching source entries. Deterministic diagnostic ordering was not added: the approved contract requires deterministic operations, which are sorted.
- Filesystem preservation tests compare directory inventories and file bytes for both actions, existing-file/directory conflicts, missing folders, and no matches. Link/network/access failures use the inspection-only fake; actual privileged symbolic-link/network integration tests were not run. No tests were skipped.
- Static review found no filesystem mutation, async, Win32, or FSCTL calls in the preview subsystem. No scope deviations. Changes remain uncommitted on `codex/milestone-3-preview`; no push, merge to main, or PR was performed.

## Files added or changed

Added:

- `src/FileFlow.Core/Preview/FileRulePreviewer.cs`
- `src/FileFlow.Core/Preview/IPreviewFileSystem.cs`
- `src/FileFlow.Core/Preview/PreviewFileSystem.cs`
- `src/FileFlow.Core/Preview/PlannedOperation.cs`
- `src/FileFlow.Core/Preview/OperationPreview.cs`
- `src/FileFlow.Core/Preview/PreviewIssue.cs`
- `tests/FileFlow.Core.Tests/PreviewTests.cs`
- `tests/FileFlow.Core.Tests/PreviewFailureTests.cs`
- `docs/superpowers/plans/2026-09-09-milestone-3-preview.md`

Changed:

- `src/FileFlow.Core/FileFlow.Core.csproj`: internal adapter access for tests.
- `README.md`: current status and synchronous preview API/behavior/verification documentation.
