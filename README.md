# FileFlow

A local Windows file automation tool. FileFlow v0.1 is being built around one safety rule: show every planned Move or Copy and require explicit confirmation before execution.

## Current status

Milestone 1 provides a rule model, structural validation, extension normalization and matching. Milestone 2 adds local JSON rule persistence in Core and isolated filesystem tests. The WPF application still opens an empty starter window. Preview, execution, and the rule editor are not implemented yet. Undo is outside v0.1.

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
- `tests/FileFlow.Core.Tests`: xUnit tests of the core behavior.

## Rule validation and matching

`RuleValidator.Validate` returns either a normalized rule or field-specific errors. It preserves the rule ID and action, trims the name, normalizes absolute Windows directory paths, and rejects identical source/destination paths. New definitions default to Copy.

Extensions are supplied as separate entries. `png`, `.png`, and `.PNG` normalize to `.png`; duplicates are removed. Empty entries, wildcards, path separators, and compound extensions are rejected. `ExtensionMatcher.Matches` consumes a validated rule and checks the final filename extension, case-insensitively, against any listed extension. For example, `.gz` matches `archive.tar.gz`.

Validation and matching do not access the filesystem. They use Windows path semantics even though the core has no WPF dependency. Structural validation rejects UNC and device paths, but does not establish that a drive is local, a directory exists, or an operation is permitted. Existence, mapped network drives, unsupported links/junctions, conflicts, and execution-time changes will be checked in later milestones before any operation is allowed.

## Rule persistence (Milestone 2)

`FileFlow.Core.Persistence.FileRuleStore` defaults to `%LOCALAPPDATA%\FileFlow\rules.json`. Pass an explicit storage directory to its constructor for isolated storage. All persistence tests use unique OS temporary directories; they never use the default store location.

`Load()` and `Save(IReadOnlyList<FileRule>)` return `RuleStoreResult`. Check `IsSuccess` before using `Rules`. Success contains the loaded or saved snapshot, including an empty collection on first launch. Failure contains a `RulePersistenceError` with `InvalidData`, `UnsupportedSchemaVersion`, or `IoError`, a descriptive message, and null rules. Callers must display/handle the error; a failed load must not be treated as an empty rule set. The starter WPF app is not wired to storage yet.

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
