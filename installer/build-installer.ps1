param(
    [switch]$FrameworkDependent,
    [string]$Version = "0.7.4.2"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish\win-x64"
$outputDir = Join-Path $PSScriptRoot "output"
$project = Join-Path $repoRoot "WinLaunch\WinLaunch.csproj"

function Find-InnoCompiler {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $null
}

Write-Host "==> Publishing WinLaunch ($(
    if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }
))..." -ForegroundColor Cyan

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
    "/p:SelfContained=$(
        if ($FrameworkDependent) { 'false' } else { 'true' }
    )",
    "/p:PublishReadyToRun=true",
    "/p:DebugType=none",
    "/p:DebugSymbols=false"
)

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $publishDir "WinLaunch.exe"
if (-not (Test-Path $exe)) {
    throw "Publish succeeded but WinLaunch.exe was not found at $exe"
}

$sizeMb = [math]::Round((Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "    Published to $publishDir ($sizeMb MB)" -ForegroundColor Green

$iscc = Find-InnoCompiler
if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 is not installed." -ForegroundColor Yellow
    Write-Host "Install it with: winget install --id JRSoftware.InnoSetup -e"
    Write-Host "Then re-run: .\installer\build-installer.ps1"
    Write-Host ""
    Write-Host "Publish output is ready at: $publishDir"
    exit 0
}

Write-Host "==> Building installer with Inno Setup..." -ForegroundColor Cyan

if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$runtimeCheck = if ($FrameworkDependent) { "1" } else { "0" }
$iss = Join-Path $PSScriptRoot "WinLaunch.iss"

& $iscc "/DMyAppVersion=$Version" "/DRuntimeCheckEnabled=$runtimeCheck" $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE"
}

$setup = Get-ChildItem $outputDir -Filter "WinLaunch-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) {
    throw "Installer build finished but no setup exe was produced in $outputDir"
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Installer: $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 1)) MB)"
