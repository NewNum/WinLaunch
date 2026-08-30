param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Tag = "",

    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish\win-x64"
$outputDir = Join-Path $PSScriptRoot "output"
$objDir = Join-Path $PSScriptRoot "obj"
$project = Join-Path $repoRoot "WinLaunch\WinLaunch.csproj"
$testProject = Join-Path $repoRoot "WinLaunch.Tests\WinLaunch.Tests.csproj"
$solution = Join-Path $repoRoot "WinLaunch.sln"

if (-not $Tag) {
    $Tag = "v$Version"
}

function Find-Tool {
    param(
        [string]$Name,
        [string[]]$Candidates
    )

    foreach ($path in $Candidates) {
        if ($path -and (Test-Path $path)) {
            return $path
        }
    }

    $fromPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    return $null
}

function Get-AssemblyVersion {
    param([string]$AssemblyInfoPath)

    foreach ($line in Get-Content $AssemblyInfoPath) {
        $trimmed = $line.Trim()
        if ($trimmed -match '^\[assembly:\s*AssemblyVersion\("([^"]+)"\)\]') {
            return $Matches[1]
        }
    }

    throw "Could not read AssemblyVersion from $AssemblyInfoPath"
}

function Get-PreviousTag {
    param([string]$CurrentTag)

    Push-Location $repoRoot
    try {
        $prevTag = git describe --tags --abbrev=0 "$CurrentTag^" 2>$null
        if ($LASTEXITCODE -eq 0 -and $prevTag) {
            return $prevTag
        }

        $tags = git tag -l "v*" --sort=-v:refname
        foreach ($tag in $tags) {
            if ($tag -ne $CurrentTag) {
                return $tag
            }
        }

        return $null
    }
    finally {
        Pop-Location
    }
}

function Get-ReleaseChangelog {
    param(
        [string]$CurrentTag,
        [string]$PreviousTag
    )

    Push-Location $repoRoot
    try {
        if ($PreviousTag) {
            $header = "Changes since $PreviousTag"
            $commits = @(git log "$PreviousTag..$CurrentTag" --pretty=format:"- %s (%h)" --no-merges 2>$null)
            if ($commits.Count -eq 0) {
                $commits = @(git log "$PreviousTag..HEAD" --pretty=format:"- %s (%h)" --no-merges 2>$null)
            }
        }
        else {
            $header = "All changes in this release"
            $commits = @(git log --pretty=format:"- %s (%h)" --no-merges 2>$null)
        }

        return @{
            Header  = $header
            Commits = $commits
        }
    }
    finally {
        Pop-Location
    }
}

function Write-ReleaseNotes {
    param(
        [string]$Version,
        [string]$Tag,
        [string]$PreviousTag,
        [string[]]$Commits,
        [hashtable]$Checksums,
        [string]$OutputPath
    )

    $prevLine = if ($PreviousTag) { $PreviousTag } else { "(none — first tagged release)" }
    $commitBlock = if ($Commits.Count -gt 0) {
        ($Commits -join "`n")
    }
    else {
        "- No commit messages found in range."
    }

    $notes = @"
## WinLaunch $Version

### .NET 10

This release targets **.NET 10** (net10.0-windows, x64). The project was migrated from .NET Framework 4.8.

| Artifact | Notes |
| --- | --- |
| `WinLaunch-Setup-$Version.exe` | Inno Setup installer, **self-contained** (includes .NET 10 runtime) |
| `WinLaunch-$Version.msi` | WiX MSI installer, **self-contained** |
| `WinLaunch-$Version-portable.zip` | Portable build; create a `Data` folder next to `WinLaunch.exe` for portable config |

- **OS**: Windows 10 or later, x64
- **Runtime**: not required for the attached installers (self-contained publish)
- **Source**: build with [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Changes

Previous tag: $prevLine

$commitBlock

### MD5 checksums

| File | MD5 |
| --- | --- |
| `WinLaunch-Setup-$Version.exe` | ``$($Checksums.Exe)`` |
| `WinLaunch-$Version.msi` | ``$($Checksums.Msi)`` |
| `WinLaunch-$Version-portable.zip` | ``$($Checksums.Zip)`` |
"@

    Set-Content -Path $OutputPath -Value $notes -Encoding UTF8
}

Write-Host "==> WinLaunch release build $Version ($Tag)" -ForegroundColor Cyan

$assemblyInfo = Join-Path $repoRoot "WinLaunch\Properties\AssemblyInfo.cs"
$assemblyVersion = Get-AssemblyVersion $assemblyInfo
if ($assemblyVersion -ne $Version) {
    throw "Tag version '$Version' does not match AssemblyInfo version '$assemblyVersion'. Update AssemblyInfo.cs before tagging."
}

if (-not $SkipTests) {
    Write-Host "==> Restore, build, test..." -ForegroundColor Cyan
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    dotnet build $solution -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    dotnet test $testProject -c Release -p:Platform=x64 --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }
}

Write-Host "==> Publish (self-contained)..." -ForegroundColor Cyan
Get-Process WinLaunch -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "-o", $publishDir,
    "/p:SelfContained=true",
    "/p:PublishReadyToRun=true",
    "/p:DebugType=none",
    "/p:DebugSymbols=false"
)
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not (Test-Path (Join-Path $publishDir "WinLaunch.exe"))) {
    throw "Publish succeeded but WinLaunch.exe was not found."
}

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}
if (-not (Test-Path $objDir)) {
    New-Item -ItemType Directory -Path $objDir | Out-Null
}

Write-Host "==> Portable ZIP..." -ForegroundColor Cyan
$portableRoot = Join-Path $objDir "portable"
if (Test-Path $portableRoot) {
    Remove-Item $portableRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $portableRoot | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $portableRoot -Recurse -Force
$dataDir = Join-Path $portableRoot "Data"
New-Item -ItemType Directory -Path $dataDir | Out-Null
Set-Content -Path (Join-Path $dataDir "README.txt") -Value "Portable mode: settings and Items.xml are stored in this folder." -Encoding UTF8

$zipPath = Join-Path $outputDir "WinLaunch-$Version-portable.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "==> Inno Setup EXE..." -ForegroundColor Cyan
$iscc = Find-Tool "ISCC.exe" @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
if (-not $iscc) {
    throw "Inno Setup 6 (ISCC.exe) not found. Install with: winget install JRSoftware.InnoSetup"
}

$iss = Join-Path $PSScriptRoot "WinLaunch.iss"
& $iscc "/DMyAppVersion=$Version" "/DRuntimeCheckEnabled=0" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

$setupExe = Join-Path $outputDir "WinLaunch-Setup-$Version.exe"
if (-not (Test-Path $setupExe)) {
    throw "Expected installer not found: $setupExe"
}

Write-Host "==> WiX MSI..." -ForegroundColor Cyan
$msiPath = Join-Path $outputDir "WinLaunch-$Version.msi"
if (Test-Path $msiPath) {
    Remove-Item $msiPath -Force
}

function Build-MsiWithWix7 {
    param(
        [string]$WixExe,
        [string]$PublishDir,
        [string]$Version,
        [string]$OutputPath,
        [string]$ScriptRoot,
        [string]$OutputDir
    )

    $packageWxs = Join-Path $ScriptRoot "WinLaunch.Package.wxs"
    & $WixExe build `
        -d "PublishDir=$PublishDir" `
        -d "ProductVersion=$Version" `
        $packageWxs `
        -o $OutputPath
    if ($LASTEXITCODE -ne 0) { throw "WiX build failed" }

    Get-ChildItem $OutputDir -Filter "cab*.cab" -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $OutputDir -Filter "*.wixpdb" -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host "    Built with WiX CLI 7" -ForegroundColor Green
}

function Build-MsiWithWix3 {
    param(
        [string]$HeatExe,
        [string]$CandleExe,
        [string]$LightExe,
        [string]$PublishDir,
        [string]$Version,
        [string]$OutputPath,
        [string]$ScriptRoot,
        [string]$ObjDir
    )

    $generatedWxs = Join-Path $ObjDir "GeneratedFiles.wxs"
    if (Test-Path $generatedWxs) {
        Remove-Item $generatedWxs -Force
    }

    & $HeatExe dir $PublishDir `
        -cg PublishFiles `
        -gg `
        -sfrag `
        -srd `
        -dr APPINSTALL `
        -var var.PublishDir `
        -out $generatedWxs
    if ($LASTEXITCODE -ne 0) { throw "WiX heat failed" }

    $wxs = Join-Path $ScriptRoot "WinLaunch.wxs"
    $candleOutDir = Join-Path $ObjDir ""
    & $CandleExe -nologo `
        -dPublishDir=$PublishDir `
        -dProductVersion=$Version `
        $wxs $generatedWxs `
        -out $candleOutDir
    if ($LASTEXITCODE -ne 0) { throw "WiX candle failed" }

    & $LightExe -nologo `
        -ext WixUIExtension `
        (Join-Path $ObjDir "WinLaunch.wixobj") `
        (Join-Path $ObjDir "GeneratedFiles.wixobj") `
        -out $OutputPath
    if ($LASTEXITCODE -ne 0) { throw "WiX light failed" }

    Write-Host "    Built with WiX Toolset v3" -ForegroundColor Green
}

$wix = Find-Tool "wix.exe" @(
    "$env:ProgramFiles\WiX Toolset v7.0\bin\wix.exe",
    "$env:LOCALAPPDATA\Programs\WiX Toolset v7.0.0\bin\wix.exe"
)
$heat = Find-Tool "heat.exe" @(
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin\heat.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\heat.exe",
    "$env:WIX\bin\heat.exe"
)
$candle = Find-Tool "candle.exe" @(
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin\candle.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\candle.exe",
    "$env:WIX\bin\candle.exe"
)
$light = Find-Tool "light.exe" @(
    "${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin\light.exe",
    "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\light.exe",
    "$env:WIX\bin\light.exe"
)

$builtMsi = $false
if ($wix) {
    Build-MsiWithWix7 `
        -WixExe $wix `
        -PublishDir $publishDir `
        -Version $Version `
        -OutputPath $msiPath `
        -ScriptRoot $PSScriptRoot `
        -OutputDir $outputDir
    $builtMsi = $true
}
elseif ($heat -and $candle -and $light) {
    Build-MsiWithWix3 `
        -HeatExe $heat `
        -CandleExe $candle `
        -LightExe $light `
        -PublishDir $publishDir `
        -Version $Version `
        -OutputPath $msiPath `
        -ScriptRoot $PSScriptRoot `
        -ObjDir $objDir
    $builtMsi = $true
}
else {
    throw "No WiX toolset found. Install WiX CLI 7 (winget install WiXToolset.WiXCLI) or WiX v3 (choco install wixtoolset)."
}

if (-not $builtMsi -or -not (Test-Path $msiPath)) {
    throw "MSI build did not produce $msiPath"
}

Write-Host "==> Checksums and release notes..." -ForegroundColor Cyan
$checksums = @{
    Exe = (Get-FileHash $setupExe -Algorithm MD5).Hash.ToLowerInvariant()
    Msi = (Get-FileHash $msiPath -Algorithm MD5).Hash.ToLowerInvariant()
    Zip = (Get-FileHash $zipPath -Algorithm MD5).Hash.ToLowerInvariant()
}

$previousTag = Get-PreviousTag -CurrentTag $Tag
$changelog = Get-ReleaseChangelog -CurrentTag $Tag -PreviousTag $previousTag
$notesPath = Join-Path $outputDir "release-notes.md"
Write-ReleaseNotes `
    -Version $Version `
    -Tag $Tag `
    -PreviousTag $previousTag `
    -Commits $changelog.Commits `
    -Checksums $checksums `
    -OutputPath $notesPath

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  EXE : $setupExe"
Write-Host "  MSI : $msiPath"
Write-Host "  ZIP : $zipPath"
Write-Host "  Notes: $notesPath"
Write-Host ""
Write-Host "MD5:"
Write-Host "  EXE $($checksums.Exe)"
Write-Host "  MSI $($checksums.Msi)"
Write-Host "  ZIP $($checksums.Zip)"
