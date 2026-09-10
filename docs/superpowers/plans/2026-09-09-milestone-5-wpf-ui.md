# Milestone 5 — approved implementation plan

Approved in the task conversation. Goal: launch, saved rules, create/edit/delete, Run, read-only Preview, explicit Execute, busy feedback, ordered results. No Milestone 6, pushes, merges or PRs.

## Constraints and decisions
- Core stays net8.0 and its APIs/semantics/schema remain unchanged. App stays net8.0-windows. Retarget only existing tests to net8.0-windows and reference App; no new projects or packages.
- Lightweight MVVM in App. ViewModels have no Window, MessageBox or folder-picker dependency; use constructor-injected dialog/storage/workflow interfaces.
- Native owned editor and run dialogs. Shared dark WPF resources, compact Windows utility density, native chrome, selectable long paths, virtualized lists, keyboard focus and accessible labels. No theme framework.
- Validate/normalize only through RuleValidator; comma-separated extension tokenizer preserves invalid empty entries. New action Copy. Folder availability checks happen only on Run.
- FileRuleStore startup failure is an error, never empty success. Retry allowed, no reset/recovery. Persist proposed collections before publishing UI changes. Preserve drafts on failures.
- Run and Execute use Task.Run wrappers over synchronous Core. Preserve exact preview identity, operations order and eligibility; guard duplicate activation. After any submission the preview stays consumed. No rescan, progress callbacks, or cancellation.
- Indeterminate execution feedback then ordered Core results, with partial-failure/no-rollback and unknown-outcome messages. Prevent closing during preview generation/execution.
- Per-user named mutex retained across application lifetime. Different Windows users do not block each other. No IPC.

## Sequential tasks
1. Fast-forward approved branch to merged Milestone 4 and verify 289-test Release baseline.
2. Add MVVM primitives, service boundaries, startup, rules load/empty/error/retry and mutex.
3. Reusable editor, Core validation, folder picking, persistence-first create/edit/delete.
4. Responsive preview, status presentation, ordering/virtualization and guards.
5. Explicit exact-instance execution, permanent consumption, busy state and results.
6. Shared styles, long paths, keyboard behavior, accessibility, resizing and DPI.
7. Automated integration/regression verification, sample-folder smoke checks and README documentation.

## Automated verification
Startup success/empty/error/retry, save failure preserves drafts, create/edit/delete and IDs/order, invalid input/normalization, cancellation, duplicate Run/Execute guards, all preview statuses, exact preview reference, ordered results/all outcomes, fresh-preview requirement, unexpected errors and busy/closing recovery, background scheduling, mutex contention/release. Use deterministic fakes and owned temporary directories, never user folders. Release build/test and git diff --check.

## Manual verification
First launch; create/edit/delete; restart; Copy/Move; conflicts/no matches/stale preview/partial failure; long paths; large scrolling; keyboard; resize/high DPI; close while busy; second instance. Record executed checks separately from any checks that require an interactive user session.

## Excluded
Undo/rollback/history, duplicate rules, watching/scheduling/recursion, extra conditions/templates/community rules, import/export/recovery, overwriting/renaming/conflict skipping/resolution, cancellation, incremental progress architecture, jobs/services/IPC, cloud/accounts/AI/telemetry/plugins, Settings placeholders/Mica/backdrops/rendering/animations/frameworks, DI/buses/navigation frameworks, heavyweight UI automation, installers/updaters/packaging and all Milestone 6 work.
