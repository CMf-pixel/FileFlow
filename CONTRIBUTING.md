# Contributing to FileFlow

Thanks for helping improve FileFlow. Bug reports, documentation corrections, tests, and focused fixes are welcome. Please open an issue to discuss large features, architectural changes, or broad scope changes before investing substantial work.

## Report a bug

Use the bug report form and include the FileFlow version, Windows version, clear reproduction steps, expected behavior, and actual behavior. Attach logs only when useful, and remove usernames, personal folder names, filenames, and other private data first.

## Develop locally

Development requires Windows and the .NET 8 SDK `8.0.400` or newer within the .NET 8.0 SDK line.

```powershell
dotnet restore FileFlow.sln
dotnet test FileFlow.sln --configuration Release --no-restore
dotnet build FileFlow.sln --configuration Release --no-restore
dotnet run --project src/FileFlow.App/FileFlow.App.csproj --configuration Release
```

Keep changes focused, preserve FileFlow's preview-before-execution safety model, and add meaningful tests when behavior changes. Before submitting a pull request, run the Release test and build commands above and describe the behavior change and validation performed.

By contributing, you agree that your contribution is licensed under the repository's [MIT License](LICENSE).
