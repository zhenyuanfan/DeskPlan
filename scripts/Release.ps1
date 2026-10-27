param([string]$DotNet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    [xml]$project = Get-Content 'src/DeskPlan/DeskPlan.csproj'
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
    $releaseDir = Join-Path $repoRoot "artifacts/releases/v$version"
    $zip = Join-Path $releaseDir "DeskPlan-v$version-win-x64.zip"
    if (Test-Path -LiteralPath $zip) { throw "Release already exists: $zip" }
    $staging = Join-Path $releaseDir ('build-' + [Guid]::NewGuid().ToString('N'))
    $package = Join-Path $staging 'DeskPlan'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    & $DotNet test DeskPlan.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & $DotNet run --project tools/DeskPlan.VisualChecks/DeskPlan.VisualChecks.csproj -c Release -- artifacts/visual-checks
    if ($LASTEXITCODE -ne 0) { throw 'Offscreen layout checks failed.' }
    & $DotNet publish src/DeskPlan/DeskPlan.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath 'README.md','CHANGELOG.md','THIRD-PARTY-NOTICES.md' -Destination $package
    Copy-Item -LiteralPath 'licenses' -Destination $package -Recurse

    $assets = Get-Content 'src/DeskPlan/obj/project.assets.json' -Raw | ConvertFrom-Json
    $framework = $assets.project.frameworks.PSObject.Properties | Select-Object -First 1
    foreach ($runtime in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $dependency = $framework.Value.downloadDependencies | Where-Object name -EQ $runtime
        if (!$dependency) { throw "Missing runtime dependency: $runtime" }
        $runtimeVersion = ($dependency.version -replace '[\[\] ]','').Split(',')[0]
        $runtimeDir = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($runtime.ToLowerInvariant() + '/' + $runtimeVersion)
            if (Test-Path -LiteralPath $candidate) { $runtimeDir = $candidate; break }
        }
        if (!$runtimeDir) { throw "Missing restored runtime: $runtime" }
        foreach ($notice in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
            $source = Join-Path $runtimeDir $notice
            if (Test-Path -LiteralPath $source) {
                Copy-Item -LiteralPath $source -Destination (Join-Path $package "licenses/$runtime-$notice")
            }
        }
    }
    $forbidden = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object {
        $_.Name -match '\.(db|sqlite|sqlite3|pdb)(-|$)' -or $_.Name -eq 'settings.json'
    }
    if ($forbidden) { throw 'Private data or debug symbols found in package.' }
    Compress-Archive -LiteralPath $package -DestinationPath $zip -CompressionLevel Optimal
    $digest = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $releaseDir 'SHA256SUMS.txt'), "$digest  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Release ready: $zip"
    Write-Output "SHA256: $digest"
} finally { Pop-Location }
