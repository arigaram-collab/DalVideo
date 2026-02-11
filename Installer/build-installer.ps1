<#
.SYNOPSIS
    DalVideo 설치 프로그램 빌드 스크립트

.DESCRIPTION
    1. dotnet publish로 self-contained 빌드
    2. FFmpeg 존재 여부 확인
    3. Inno Setup 컴파일러(ISCC.exe)로 설치 프로그램 생성

.EXAMPLE
    .\build-installer.ps1
#>

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$InstallerDir = $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  DalVideo Installer Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: dotnet publish
Write-Host "[1/3] Publishing DalVideo (self-contained, win-x64)..." -ForegroundColor Yellow
$publishDir = Join-Path $ProjectRoot "bin\publish\installer"

if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

& dotnet publish "$ProjectRoot\DalVideo.csproj" `
    -p:PublishProfile=InstallerProfile `
    -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed." -ForegroundColor Red
    exit 1
}

Write-Host "Publish completed: $publishDir" -ForegroundColor Green
Write-Host ""

# Step 2: Check FFmpeg
Write-Host "[2/3] Checking FFmpeg..." -ForegroundColor Yellow
$ffmpegDir = Join-Path $InstallerDir "ffmpeg"
$ffmpegPath = Join-Path $ffmpegDir "ffmpeg.exe"

if (Test-Path $ffmpegPath) {
    Write-Host "FFmpeg found: $ffmpegPath" -ForegroundColor Green
} else {
    Write-Host "WARNING: FFmpeg not found at $ffmpegPath" -ForegroundColor DarkYellow
    Write-Host "  FFmpeg will NOT be included in the installer." -ForegroundColor DarkYellow
    Write-Host "  To include FFmpeg:" -ForegroundColor DarkYellow
    Write-Host "    1. Download from https://www.gyan.dev/ffmpeg/builds/" -ForegroundColor DarkYellow
    Write-Host "    2. Place ffmpeg.exe in: $ffmpegDir" -ForegroundColor DarkYellow
    Write-Host ""
}

# Step 3: Run Inno Setup Compiler
Write-Host "[3/3] Building installer with Inno Setup..." -ForegroundColor Yellow

$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($p in $isccPaths) {
    if (Test-Path $p) {
        $iscc = $p
        break
    }
}

if (-not $iscc) {
    Write-Host "ERROR: Inno Setup 6 not found." -ForegroundColor Red
    Write-Host "  Download from: https://jrsoftware.org/isdownload.php" -ForegroundColor Red
    exit 1
}

Write-Host "Using ISCC: $iscc" -ForegroundColor Gray

$issFile = Join-Path $InstallerDir "DalVideo.iss"
& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Inno Setup compilation failed." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$outputDir = Join-Path $InstallerDir "Output"
$setupFile = Get-ChildItem -Path $outputDir -Filter "DalVideo-Setup-*.exe" | Select-Object -First 1
if ($setupFile) {
    Write-Host "Installer: $($setupFile.FullName)" -ForegroundColor Green
}
