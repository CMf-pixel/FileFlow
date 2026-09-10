# Releasing FileFlow

This checklist describes the public `v0.1.0` release. Run commands from the repository root on Windows. Do not treat hosted CI, a real screenshot, or real GUI and no-runtime smoke testing as complete until each has actually been performed and its evidence recorded.

## 1. Prepare the release commit

- Confirm the intended version is `0.1.0` everywhere it is exposed in application and package metadata.
- Replace `Unreleased` in `CHANGELOG.md` with the actual release date in `YYYY-MM-DD` format **before** creating the final tag. The dated changelog must be part of the approved commit that receives the tag.
- Update the GitHub repository About description. It currently advertises Undo; replace it with: `A local Windows file automation tool that previews every planned Move or Copy before execution.`
- Optionally add accurate repository topics after reviewing them.
- Add a real application screenshot only after user approval. The image must not expose usernames, personal folders, desktop contents, unrelated applications, or other private data. Save the approved image as `docs/images/fileflow-main.png` and replace the README placeholder.
- Keep runtime-license discovery simple. Use only clear license files or metadata exposed by the SDK and published runtime. If the required notices or redistribution terms are ambiguous, stop and report the uncertainty instead of inventing or inferring a license inventory.
- Review the complete diff, confirm only intended files are present, and ensure the source worktree is clean before tagging.
- Obtain explicit approval before merging a pull request, pushing repository changes, or creating/updating a pull request.

`SECURITY.md` and a pull request template are deferred beyond this release.

## 2. Verify source

```powershell
dotnet restore FileFlow.sln
dotnet test FileFlow.sln --configuration Release --no-restore
dotnet build FileFlow.sln --configuration Release --no-restore
git diff --check
```

Record the command output and commit SHA. Confirm the `windows-latest` CI workflow passes for pull requests and `main`; do not mark hosted CI complete until the GitHub run succeeds.

## 3. Publish the portable archive

Run:

```powershell
pwsh -File ./scripts/Publish-Portable.ps1
```

Packaging requires PowerShell 7 or later (`pwsh`) in addition to the .NET 8 SDK.

The script publishes a clean self-contained Windows x64 application, stages it under `artifacts/staging/<guid>`, and creates `artifacts/FileFlow-v0.1.0-win-x64.zip`. It refuses to overwrite an existing archive. If rerunning is necessary, manually preserve the existing artifact outside the output path before rerunning; do not weaken the overwrite guard.

Verify the publish is clean and contains the executable, required runtime files, application assemblies, and applicable license notices. Confirm the ZIP name and version metadata are exact, inspect its contents, extract it to a different folder, and record file size and SHA-256 evidence.

## 4. Manual smoke test

Use a disposable Windows account or VM with fresh FileFlow storage and only test-owned temporary files. Test the application from the published `FileFlow.App.exe`, not a development build.

- First launch, rule create/edit/delete, restart, and persisted state.
- Copy and Move, checking file bytes and final source/destination state.
- Close an idle preview and confirm zero filesystem mutation.
- Existing destination conflict and zero mutation.
- Stale preview rejection and zero mutation.
- Extension matching, nested directories ignored, and no-match behavior.
- Second-instance handling.
- Extract the ZIP into a different folder and run it there.
- Run on Windows x64 without a separately installed .NET runtime.

Record OS details, test paths, results, artifact SHA-256, and screenshots or logs after sanitization. Real GUI smoke and no-runtime smoke remain pending until performed on the published executable.

## 5. Tag and draft the GitHub release

The tag workflow on `v*` validates that the tag matches the application version and that `CHANGELOG.md` contains a dated release heading before it restores, tests, builds, packages, or creates a draft. It then reruns tests and build, packages the portable ZIP, and creates an **unpublished draft** release with GitHub CLI equivalent to:

```powershell
gh release create v0.1.0 artifacts/FileFlow-v0.1.0-win-x64.zip --draft --verify-tag --title "FileFlow v0.1.0" --notes-file CHANGELOG.md
```

Before creating a draft, the workflow lists releases, including drafts, and stops if the tag already exists or the lookup fails. It never replaces an existing asset. Do not publish automatically.

After all preparation evidence is reviewed, obtain separate explicit approval to create and push the annotated `v0.1.0` tag pointing to the clean, tested release commit.

## 6. Review and publish

- Confirm the draft tag, title, changelog notes, and archive name. Record the size and SHA-256 of the exact workflow-built asset being reviewed; a separate local build need not have an identical ZIP hash.
- Download the draft asset, extract all files, and repeat a focused launch plus Copy/Move/preview safety check from the downloaded artifact.
- Confirm the GitHub About description and approved screenshot are correct.
- Obtain separate explicit approval to publish the public GitHub release, then publish the draft only after all required evidence is complete and reviewed.

## Recovery

If the tag workflow fails, inspect the existing release or draft and its assets before taking action. Preserve logs and artifacts. After approval, manually remove only a failed **draft** created by the unsuccessful attempt, then rerun the workflow. Never delete a published release as recovery, never replace published assets, and never move or recreate the tag without an explicit reviewed recovery decision.

## Local preparation verification — 2026-09-10

These checks used Windows x64, .NET SDK 8.0.424, and bundled runtime 8.0.30, with Milestone 6 changes uncommitted on top of `53149ef`. They are preparation evidence, not verification of a final tagged build.

- Release suite: 329 passed, zero failed or skipped. Release solution build: zero warnings or errors.
- Portable publish: `FileFlow-v0.1.0-win-x64.zip`, 72,086,597 bytes (68.75 MiB); 159.93 MiB extracted, 468 files.
- ZIP SHA-256: `9AAFF5E80974E0E91551376764DCB7F816E81AAE7FC37E9867AE2D16E55BE7A4`.
- Extracted archive inspection verified application version/product metadata, self-contained runtime configuration, and exact license/notice hashes. No tests, source trees, PDBs, development documents, or manual-test data were present.
- Packaging rejected an existing ZIP without changing it and stopped after a simulated native publish failure without producing a ZIP.
- Workflow/issue YAML and embedded PowerShell parsed successfully. Local fixtures checked malformed/mismatched tags, Unreleased/invalid dates, and valid dated headings. The existing-release guard stopped on lookup failure and an existing tag. No real release-creation command was executed.
- Still pending: hosted CI, real GUI and no-runtime smoke tests, approved sanitized screenshot, manual About correction, actual changelog release date, and separately approved publication steps above.
