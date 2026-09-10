# FileFlow

A local Windows file automation tool. FileFlow v0.1 is being built around one safety rule: show every planned Move or Copy and require explicit confirmation before execution.

## Current status

Milestone 1 provides a rule model, structural validation, extension normalization and matching. Milestone 2 adds local JSON rule persistence. Milestone 3 adds synchronous, read-only preview in Core with isolated filesystem tests. Milestone 4 adds controlled Core execution of an explicitly approved preview. Milestone 5 connects these APIs to a compact dark WPF workflow: saved rules, create/edit/delete, read-only Preview, explicit Execute, busy feedback, and ordered results. Undo and release packaging remain outside this milestone.

## Build and test

Requires Windows and the .NET 8 SDK. Run these commands from the repository root:

```powershell
dotnet build FileFlow.sln
dotnet test tests/FileFlow.Core.Tests/FileFlow.Core.Tests.csproj
dotnet run --project src/FileFlow.App/FileFlow.App.csproj
```

## Project structure

- `src/FileFlow.App`: WPF application, referencing the core library.
- `src/FileFlow.Core`: ordinary .NET library with no WPF or external package dependencies.
- `tests/FileFlow.Core.Tests`: Windows-targeted xUnit tests of Core and App presentation behavior. Core itself remains on `net8.0`; App and tests target `net8.0-windows`.

## Rule validation and matching

`RuleValidator.Validate` returns either a normalized rule or field-specific errors. It preserves the rule ID and action, trims the name, normalizes absolute Windows directory paths, and rejects identical source/destination paths. New definitions default to Copy.

Extensions are supplied as separate entries. `png`, `.png`, and `.PNG` normalize to `.png`; duplicates are removed. Empty entries, wildcards, path separators, and compound extensions are rejected. `ExtensionMatcher.Matches` consumes a validated rule and checks the final filename extension, case-insensitively, against any listed extension. For example, `.gz` matches `archive.tar.gz`.

Validation and matching do not access the filesystem. They use Windows path semantics even though the core has no WPF dependency. Structural validation rejects UNC and device paths, but does not establish that a drive is local, a directory exists, or an operation is permitted. Preview checks existence, mapped network drives, unsupported links/junctions, and conflicts. Execution independently repeats the relevant checks before any mutation.

## Rule persistence (Milestone 2)

`FileFlow.Core.Persistence.FileRuleStore` defaults to `%LOCALAPPDATA%\FileFlow\rules.json`. Pass an explicit storage directory to its constructor for isolated storage. All persistence tests use unique OS temporary directories; they never use the default store location.

`Load()` and `Save(IReadOnlyList<FileRule>)` return `RuleStoreResult`. Check `IsSuccess` before using `Rules`. Success contains the loaded or saved snapshot, including an empty collection on first launch. Failure contains a `RulePersistenceError` with `InvalidData`, `UnsupportedSchemaVersion`, or `IoError`, a descriptive message, and null rules. Callers must display/handle the error; a failed load must not be treated as an empty rule set. The WPF app displays load failures as an error state with Retry and blocks rule edits until loading succeeds. It publishes create/edit/delete changes only after storage succeeds.

The UTF-8 JSON format is:

```json
{
  "schemaVersion": 1,
  "rules": [
    {
      "id": "271df344-1e31-4ea0-b1b8-716cfa9f39e2",
      "name": "Sort images",
      "sourceDirectory": "C:\\Downloads",
      "extensions": [".png", ".jpg"],
      "action": "Copy",
      "destinationDirectory": "C:\\Pictures"
    }
  ]
}
```

Rule order and every field are preserved, including Copy/Move. All properties are required; unknown/duplicate properties, invalid rule definitions, malformed JSON, and unsupported versions are rejected. Structural validation does not require the source/destination directories to exist and does not normalize away stored values.

Saving validates the existing active document, writes a unique temporary file beside it, and calls `Flush(true)` before closing it. When an active file exists, its exact valid bytes are also staged and flushed, then published as `rules.json.bak`. Only then is the new active file published with a same-directory overwrite rename. The active file is never deleted first. This avoids the partial-failure states documented for [Windows ReplaceFile](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew). A failed active publication can leave the backup equal to the still-active file; it cannot activate the failed save. One previous backup is retained after successful replacement.

An exclusively opened `rules.json.lock` prevents overlapping FileFlow writers; contention returns an I/O error for the caller to handle. The empty lock file stays on disk, but the OS releases its lock when the stream/process closes. This coordinates FileFlow stores, not unrelated programs editing the files externally.

Corrupt/unsupported active files are preserved and block saves, including saves through a new store instance. Backups and abandoned temporary files are never automatically loaded, restored, or promoted. Cleanup only removes temporary files created by that save; if cleanup fails they remain inert. Recovery requires inspecting/preserving the files manually. There is no recovery UI, import/export, or background monitoring in this milestone. Durability still depends on the local filesystem and storage device honoring flushes; the active rename and backup update are separate steps.

Run the Milestone 2 tests with:

```powershell
dotnet test tests/FileFlow.Core.Tests/FileFlow.Core.Tests.csproj --filter FullyQualifiedName~FileRuleStoreTests
dotnet test FileFlow.sln
dotnet build FileFlow.sln --configuration Release
```

## Read-only preview (Milestone 3)

Pass a rule to `FileFlow.Core.Preview.FileRulePreviewer.CreatePreview(FileRule)`. This synchronous API does not load or save rules, execute operations, create directories, open write streams, or perform write probes. The WPF application invokes it off the UI thread; Core has no background-service architecture.

```csharp
var preview = new FileRulePreviewer().CreatePreview(rule);
foreach (var operation in preview.Operations)
{
    Console.WriteLine($"{operation.Action}: {operation.SourcePath} -> {operation.DestinationPath}");
}
foreach (var issue in preview.Issues)
{
    Console.WriteLine($"{issue.Code}: {issue.Path ?? issue.PropertyName}: {issue.Message}");
}
Console.WriteLine(preview.Status); // NoMatches explicitly means zero files matched.
```

`OperationPreview` contains an immutable normalized rule snapshot, read-only ordered operations, and typed blocking issues. A structurally invalid rule returns validation issues with a null normalized rule. Each `PlannedOperation` preserves the original filename and records full source/destination paths, Copy/Move action, source byte length, and last-write time in UTC. Operations are ordered by ordinal case-insensitive filename, then ordinal filename to break ties.

Status precedence is explicit:

1. `Incomplete`: any required scan or inspection failed, even if conflicts were also found. Known operations remain available for diagnosis; the list may be partial.
2. `Blocked`: the completed evaluation found blocking issues, including confirmed validation/preflight rejection.
3. `NoMatches`: the completed evaluation found no issues and zero operations.
4. `Ready`: the completed evaluation found operations and no issues.

`CanExecute` describes preview eligibility only: it is true exclusively for `Ready` with operations and no issues. It does not record whether execution has already been attempted. All issues block the entire batch; a conflict is never overwritten, renamed, or skipped automatically. Resolve the issue and generate a fresh preview.

Preview enumerates immediate source entries only, matches ordinary files using the existing extension matcher, and ignores ordinary subdirectories without opening them. Missing source/destination folders, equivalent normalized directory paths, existing destination entries, and duplicate case-insensitive destination names block the batch. Missing destinations are never created. Access errors, vanished source entries, metadata failures, and interrupted enumeration are reported instead of silently omitted.

UNC/device paths and mapped network drives are unsupported. Attribute checks reject reparse points in source/destination directory components (root first) and all encountered direct source entries, including nonmatching entries. This deliberately conservative rule also rejects junctions, symbolic links, and other reparse entries such as some cloud placeholders. Preview does not follow links, parse reparse tags, use FSCTL calls, or traverse source subdirectories.

Preview is an observation, not an atomic filesystem snapshot. Paths, metadata, permissions, and destination contents may change during or after inspection. It does not reserve names, test write permissions, or provide execution-time guarantees; the executor independently revalidates. Link and network failure paths are covered with an internal inspection-only test adapter. Real filesystem safety tests use unique OS temporary directories and compare directory inventories and file bytes before and after preview.

Run preview tests and verification with:

```powershell
dotnet test tests/FileFlow.Core.Tests/FileFlow.Core.Tests.csproj --filter FullyQualifiedName~Preview
dotnet test FileFlow.sln
dotnet build FileFlow.sln --configuration Release
```

## Controlled execution (Milestone 4)

`FileFlow.Core.Execution.FileOperationExecutor.Execute(OperationPreview approvedPreview)` executes the exact previously generated preview, synchronously. The caller must show that preview and obtain explicit approval before calling. Core cannot verify human consent. There is no `FileRule` overload, rescan, or automatic preview regeneration; newly added source files are ignored.

```csharp
// This method is called only after explicit approval of this exact preview.
static ExecutionResult ExecuteApprovedPreview(OperationPreview approvedPreview)
{
    return new FileOperationExecutor().Execute(approvedPreview);
}
```

Use the `FileFlow.Core.Preview` and `FileFlow.Core.Execution` namespaces. Milestone 5 calls this API away from the UI thread. Core remains synchronous and has no scheduling or background service.

Each preview object permits one attempt per process, including rejected attempts. A shared `ConditionalWeakTable` and atomic flag prevent reuse through another executor instance or concurrent submission. Preview data stays immutable; the guard adds no persistence, sessions, messaging, or lifecycle service. After any attempt, generate a fresh preview and obtain approval again. `CanExecute` remains preview eligibility only. Null input and unexpected programming errors throw; an unexpected error does not release a claimed preview.

Before the first mutation, the executor requires `Ready`, at least one operation, and zero issues. It checks the entire approved batch: canonical local paths and unchanged filename/action, ordinary source files, exact source length and last-write UTC, existing ordinary directory ancestors inspected root-first, supported drives, and absent destination names. Source/destination reparse points, network drives, missing parents, inspection failures, and file or directory conflicts reject the batch. The separate execution filesystem boundary has only three inspection methods and non-overwriting Copy/Move; preview retains its original read-only boundary.

**Preflight failure means zero mutations.** Every operation is `NotAttempted`, with ordered diagnostics identifying the failing paths and zero-based operation indexes. Repairing a stale condition does not make that preview reusable. Preflight does not create directories or probe write permissions.

After preflight, operations run sequentially in the exact displayed order. Immediately before each operation, its source metadata, destination, drives, and ancestors are checked again. The adapter calls `File.Copy` or `File.Move` with `overwrite: false`. A destination appearing after the checks still causes the non-overwriting operation to fail.

Copy success requires a destination file of the expected length and the original source still present with matching preview metadata. Move success requires a destination file of the expected length and confirmed source absence. Cross-volume local Move is allowed using .NET's implementation; it can internally copy/delete. Returning from `File.Move` is therefore followed by final-state verification. If both entries remain, `MoveIncomplete` reports failure and preserves both. Access failures or missing parents cannot be mistaken for confirmed source removal. [Microsoft documents these cross-volume Move semantics](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move).

The first per-operation check, mutation, or verification failure stops the batch. Earlier successes remain, that operation is `Failed`, and every later operation is `NotAttempted`. No retry, rollback, overwrite, rename, skip-conflict behavior, or destructive cleanup occurs. A failed mutation may leave partial destination content or an uncertain filesystem state; results do not imply that failure left files unchanged. Expected filesystem failures, including I/O, permission, security, and path errors, become typed diagnostics with readable messages.

`ExecutionResult` defensively copies its ordered `Operations` and `Errors` collections and derives `SucceededCount`, `FailedCount`, and `NotAttemptedCount` from the operation results. Each `OperationResult` retains the original `PlannedOperation`, its status, and an optional error. Overall outcomes are:

| Outcome | Meaning |
| --- | --- |
| `Rejected` | The operation loop was never entered; `PreflightRejected` is true and all entries are `NotAttempted`. |
| `Succeeded` | Every approved operation completed and passed final-state verification. |
| `Failed` | Execution stopped without a verified successful operation. |
| `PartiallySucceeded` | At least one operation succeeded before execution stopped. |

These checks reduce races without making the filesystem transactional. Length and timestamp are not file identity or content fingerprints; same-metadata replacements and changes during or after checks can escape detection. Production does not hash files, reserve names, lock paths, or provide crash recovery. Success describes the observed postconditions, not future state. Tests verify copied bytes directly and simulate cross-volume/incomplete outcomes without depending on a second physical drive.

All real execution tests use uniquely named, owned OS temporary directories. Representative stale-preview tests compare directory inventories and file bytes after introducing staleness but before execution, then after rejection, while asserting zero mutation calls. Fault injection covers disk-full-style failures, deterministic mid-batch failures, reparse/network changes, and destination races. No tests operate on real user folders or disconnect actual drives.

Final Milestone 4 verification:

```powershell
dotnet test FileFlow.sln --configuration Release
dotnet build FileFlow.sln --configuration Release
git diff --check
```

Operation history persistence, Undo, rollback, watchers, recursive scanning, transactions, locking infrastructure, remain excluded from Core execution. No new projects or third-party packages are required.


## Desktop workflow (Milestone 5)

Launch FileFlow to see your saved rules. Create a rule with a name, source folder, comma-separated extensions such as `.png, jpg`, a Copy or Move action, and a destination folder. Copy is the default. Both folder fields support manual paths and the native Windows folder picker. Core normalizes valid extensions and paths. A fresh Create dialog hides validation errors until a field is edited or left; Save stays disabled until Core reports the rule structurally valid. Once valid, the editor shows any newly introduced errors, including related-field errors. A direct invalid save attempt reveals all errors without persisting.

A rule can be saved while a folder is unavailable. Folder existence, accessibility and supported-path checks happen when you choose Run. Only files directly inside the source folder are considered; rules never watch folders or run automatically.

Run prepares a preview without changing files. Ready previews show every planned source/destination pair and require an explicit Execute action. Blocked, Incomplete and NoMatches previews cannot execute. Incomplete lists are diagnostic and may be partial. Closing an idle preview changes nothing.

Preview generation and execution run away from the WPF dispatcher. Execution displays an indeterminate busy indicator because Core returns a complete result without progress callbacks. Closing is blocked while work is active. There is no cancellation of Copy/Move.

Results distinguish Succeeded, Failed and NotAttempted in preview order. Failures may leave filesystem changes, and completed operations are not rolled back. A preflight rejection starts no file operations. After any attempt, close the results and Run again to generate a fresh preview; the previous Execute action cannot be reused. There is no Undo.

Deleting a rule requires confirmation and affects only the saved definition. It never deletes, moves, restores, or otherwise changes your files. If saving fails, the previous saved list and editor draft remain available. Corrupt or unsupported storage is preserved and never replaced with an empty rule set. FileFlow has no automatic recovery UI.

Only one FileFlow instance may run for a Windows user, including across sessions. A second launch displays an explanation and exits. Different Windows users have independent mutex names. No IPC or command forwarding is used.

App uses small constructor-injected view models and service interfaces. Window/dialog APIs stay in views, startup and the WPF dialog adapter. Shared background/surface/text resources allow later appearance changes without changing workflow architecture; Mica/Acrylic and Settings are not implemented.

FileFlow's WPF windows use native dark captions on Windows 11, with standard system captions on older Windows or in high-contrast mode. Owned application dialogs center on their owner. The compact initial and minimum window sizes are recorded in the verification notes.

### Milestone 5 verification

```powershell
dotnet test FileFlow.sln --configuration Release
dotnet build FileFlow.sln --configuration Release
git diff --check
```

Presentation tests cover startup, persistence failures, normalization, create/edit/delete, duplicate activation, all preview statuses, exact preview identity, one-attempt behavior, ordered results, unexpected failures, background scheduling and mutex contention/recovery. Real integration tests create unique OS temporary folders for persistence, Copy/Move, cancellation and stale-preview rejection. No automated test uses the default user rules store.

Interactive smoke checklist (use temporary/sample folders for file mutations): first launch; create/edit/delete and restart; Copy and Move; no matches and conflicts; stale previews and partial failures; long paths; keyboard focus and Escape/Enter; resizing; high DPI; large-list scrolling; closing while busy; second-instance behavior. Validation records distinguish automated/offscreen checks from interactive checks still requiring a Windows desktop session.

See [Milestone 5 verification](docs/milestone-5-verification.md) for executed automated/offscreen checks and the remaining interactive smoke checklist.
