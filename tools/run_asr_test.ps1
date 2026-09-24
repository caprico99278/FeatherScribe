# Phase 0 ASR check script.
# Transcribes samples/audio/sample_001.wav with whisper.cpp and saves the text to samples/raw.
# Usage: powershell -ExecutionPolicy Bypass -File tools/run_asr_test.ps1 [-InputWav path] [-ModelSize small]
#
# Keep this file ASCII-only: Windows PowerShell 5.1 reads BOM-less files as the ANSI code page.

param(
    [string]$InputWav = "",
    [string]$ModelSize = "small",
    [int]$Threads = 4
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $InputWav) { $InputWav = Join-Path $repoRoot "samples/audio/sample_001.wav" }

$whisperCli = Join-Path $repoRoot "local/whisper/Release/whisper-cli.exe"
$modelPath = Join-Path $repoRoot "local/models/ggml-$ModelSize.bin"
$outDir = Join-Path $repoRoot "samples/raw"
$baseName = [IO.Path]::GetFileNameWithoutExtension($InputWav)
$outBase = Join-Path $outDir ($baseName + "_raw")

foreach ($required in @($whisperCli, $modelPath, $InputWav)) {
    if (-not (Test-Path $required)) {
        Write-Error "Not found: $required (run tools/setup_whisper.ps1 first)"
        exit 1
    }
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "Transcribing $InputWav ..."
$sw = [Diagnostics.Stopwatch]::StartNew()
& $whisperCli -m $modelPath -l ja -t $Threads --output-txt --output-file $outBase --no-prints -f $InputWav
$exitCode = $LASTEXITCODE
$sw.Stop()

if ($exitCode -ne 0) {
    Write-Error "whisper-cli failed with exit code $exitCode"
    exit $exitCode
}

Write-Host ""
Write-Host "Done in $($sw.Elapsed.TotalSeconds.ToString('F1'))s. Output: $outBase.txt"
Get-Content -Raw -Encoding UTF8 "$outBase.txt"
