# Changelog

All notable changes to FileFlow are documented in this file.

## [0.1.0] - Unreleased

- Added a Windows desktop workflow for creating, editing, deleting, previewing, and explicitly executing saved Move or Copy rules.
- Added case-insensitive direct-file extension matching and immediate-folder scanning.
- Added read-only previews with ordered operations and blocking diagnostics for missing directories, conflicts, unsupported network paths, and reparse points.
- Added guarded execution with whole-batch preflight validation, per-operation revalidation, non-overwriting file operations, and ordered results.
- Added local JSON rule storage at `%LOCALAPPDATA%\FileFlow\rules.json`, including validation, writer locking, and retention of one previous valid document.
- Added per-Windows-user single-instance handling.
- Added Windows-targeted automated tests for Core behavior and the desktop presentation workflow.
- Added a self-contained portable Windows x64 ZIP packaging path with runtime license notices and overwrite protection.
- Added pull request and `main` CI plus guarded tag-based preparation of unpublished draft releases.
