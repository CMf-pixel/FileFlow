# Milestone 5 verification

Validated on Windows with .NET SDK 8.0.424. The implementation starts from merged Milestone 4 (`5fd25fc`); its 289-test baseline and Release build passed before application changes. Core source and its net8.0 target are unchanged.

## Automated checks

- Presentation-only Release run: 40 passed, none failed or skipped.
- Full Release suite: 329 passed (289 existing Core cases and 40 presentation/integration cases), none failed or skipped.
- Release solution build: succeeded with zero warnings and zero errors. All three commands used `--no-restore` with the existing restored dependencies.
- Real store and workflow integration: first launch, save/reload, Copy, Move, preview cancellation, corrupt storage preservation, and destination creation after preview. All filesystem fixtures use uniquely owned OS temporary folders.
- Presentation logic: validation, persistence-first create/edit/delete, retained drafts on save failure, ordering/IDs, duplicate action guards, exact preview identity, all preview statuses and result outcomes, permanent preview consumption, and unexpected-error recovery.
- Threading and instance protection: synchronous Core work executes away from the caller; named-mutex contention, release and abandoned ownership recovery are exercised. Production mutex names include the current Windows user SID.
- WPF smoke check: creates a base WPF Application with shared resources, never FileFlow's real startup/store. Loads and lays out the main window, editor and all preview states, checks bindings and actual window/heading contrast. On supported Windows, hidden native window handles confirm the dark-title-bar attribute is enabled.

The contrast checks were observed failing before the shared-heading and explicit-derived-window style fixes, then passing after them.

## UI polish pass

- Added a shared, dependency-free native DWM caption helper for FileFlow's WPF windows. Dark mode plus caption/text colors follow the existing dark brushes. Standard Windows captions remain on older Windows and in high-contrast mode. The [documented caption attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) are supported from Windows 11 build 22000; caption/text colors are set-only and are not tested through unsupported readback calls.
- The dialog adapter explicitly sets CenterOwner after resolving ownership, with CenterScreen as the ownerless fallback.
- Fresh Create dialogs show no validation errors. Editing or leaving a field exposes its Core errors; after the form has been valid, newly introduced errors remain visible, including errors Core assigns to related fields. A direct invalid Save attempt reveals all errors and cannot persist.
- Save availability now follows the full Core validation result, independently of which errors are visible. Six focused editor regression tests cover these behaviors. No validation rules were duplicated.
- Changed the main subtitle to exactly: "Automate files with a preview before anything changes."
- Preserved compact styling and window sizes. Runtime layout checks confirmed the following WPF device-independent sizes:

| Window | Initial | Minimum | Startup placement |
| --- | --- | --- | --- |
| Main | 900 × 620 | 680 × 460 | CenterScreen |
| Create/Edit Rule | 620 × 540 | 520 × 460 | CenterOwner |
| Preview/results | 900 × 650 | 680 × 460 | CenterOwner |

Delete confirmation retains its 430-wide, content-height, non-resizable layout (minimum width 380). Fresh Create and minimum-width main-window renders were inspected after polish. These content renders do not include native title bars; native dark-mode integration is checked separately using hidden window handles.

Scoped code review found no actionable issues. Core source and target remain unchanged; view models have no WPF window dependencies. No new features, dependencies, animations, backdrops, settings, or Milestone 6 work were added. No commit, push, merge, or pull request was created.

## Offscreen rendering checks

A temporary, ignored harness under App's build-output directory renders the real WPF view content without launching FileFlow's normal startup or reading personal storage. Inspected empty/rules/load-error, valid/invalid editor, all preview statuses, processing and partial-result surfaces at normal and smaller sizes.

The harness checks actual resolved window backgrounds and foregrounds rather than assuming resource colors, captures binding errors, verifies that a busy run window refuses Close and allows it after completion, and inspects list-container creation. The 5,000-operation preview realized 5 row containers in its viewport, confirming virtualization is retained.

These are runtime/offscreen checks, not a claim of manual keyboard, mouse, native-picker, or physical-monitor testing.

## Remaining interactive smoke checklist

Use only temporary/sample folders for mutations:

- Launch the real app; create/edit/delete and restart to check persistence.
- Complete Copy and Move flows; inspect no-match, conflict and partial-failure results.
- Exercise Tab, field mnemonics, focus rings, overflow menu, Escape and safe Enter behavior.
- Open/cancel native folder pickers and verify editor ownership.
- Inspect long paths, resize windows, drag/scroll long lists, and check system/multi-monitor high DPI.
- Attempt closing during preview/execution; launch a second instance and confirm the explanation/exit.

Milestone 6 packaging, push, merge and pull-request creation are excluded.
