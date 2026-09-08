# FileFlow

A local Windows file automation tool. FileFlow v0.1 is being built around one safety rule: show every planned Move or Copy and require explicit confirmation before execution.

## Current status

Milestone 1 provides a rule model, structural validation, extension normalization and matching, and automated tests. The WPF application still opens an empty starter window. Persistence, preview, execution, and the rule editor are not implemented yet. Undo is outside v0.1.

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
