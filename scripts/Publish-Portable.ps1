#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'FileFlow must be published on Windows.' }

$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    $project = 'src/FileFlow.App/FileFlow.App.csproj'
    $version = (& dotnet msbuild $project -nologo -getProperty:Version) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the application version.' }
    $version = $version.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected application version: $version" }

    $archive = Join-Path $repo "artifacts/FileFlow-v$version-win-x64.zip"
    if (Test-Path -LiteralPath $archive) { throw "Archive already exists; preserve it elsewhere before retrying: $archive" }
    $staging = Join-Path $repo ('artifacts/staging/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null

    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
        --output $staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed. Staging retained for inspection: $staging" }

    # Use the published runtime versions and NuGet's recorded package roots, not machine-specific paths.
    $runtime = Get-Content (Join-Path $staging 'FileFlow.App.runtimeconfig.json') -Raw | ConvertFrom-Json
    $assets = Get-Content 'src/FileFlow.App/obj/project.assets.json' -Raw | ConvertFrom-Json
    $frameworks = @($runtime.runtimeOptions.includedFrameworks)
    foreach ($name in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        if ($name -notin $frameworks.name) { throw "Self-contained runtime missing: $name" }
    }
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $staging 'LICENSE')
    foreach ($framework in $frameworks) {
        $package = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
        $pack = $assets.packageFolders.PSObject.Properties.Name |
            ForEach-Object { Join-Path $_ "$package/$($framework.version)" } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if (-not $pack) { throw "Runtime license package not found: $package/$($framework.version). Stop and review; do not guess." }
        $notices = @(Get-ChildItem -LiteralPath $pack -File |
            Where-Object { $_.Name -in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') })
        if (-not ($notices.Name -match '^LICENSE(\.TXT)?$')) { throw "Runtime license missing in $pack" }
        $noticeDirectory = Join-Path $staging "licenses/$($framework.name)"
        New-Item -ItemType Directory -Path $noticeDirectory | Out-Null
        $notices | Copy-Item -Destination $noticeDirectory
    }

    foreach ($required in @('FileFlow.App.exe', 'FileFlow.App.dll', 'FileFlow.Core.dll',
            'FileFlow.App.deps.json', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $required) -PathType Leaf)) {
            throw "Required publish file missing: $required"
        }
    }
    $files = @(Get-ChildItem -LiteralPath $staging -File -Recurse -Force)
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($staging, $file.FullName)
        if ($relative -match '(^|[\\/])(tests?|src|Source|Destination|obj|docs|\.git)([\\/]|$)' -or
            $file.Name -match '(?i)(\.pdb$|\.cs$|\.xaml$|\.csproj$|\.sln$|\.md$|xunit|testhost|FileFlow\..*Tests)') {
            throw "Unexpected publish content: $relative"
        }
    }

    # ZipFile also refuses an existing destination if another publisher created it during our publish.
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)

    [pscustomobject]@{
        Archive = $archive
        Staging = $staging
        Files = $files.Count
        ZipMiB = [math]::Round((Get-Item -LiteralPath $archive).Length / 1MB, 2)
        ExpandedMiB = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
    }
}
finally { Pop-Location }
