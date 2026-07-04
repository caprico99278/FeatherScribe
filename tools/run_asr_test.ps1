# Phase 0 ASR 検証スクリプト
# samples/audio/sample_001.wav を whisper.cpp で文字起こしし samples/raw へ保存する。
# 使い方: powershell -ExecutionPolicy Bypass -File tools/run_asr_test.ps1 [-InputWav path] [-ModelSize small]

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
        Write-Error "Not found: $required (tools/setup_whisper.ps1 を先に実行してください)"
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
