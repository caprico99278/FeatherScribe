# Phase 0 整形検証スクリプト
# samples/raw のテキストを Gemma 4 (Ollama /api/chat) で整形し samples/formatted へ保存する。
# 使い方: powershell -ExecutionPolicy Bypass -File tools/run_format_test.ps1 [-InputTxt path] [-ModelTag gemma4:e4b]

param(
    [string]$InputTxt = "",
    [string]$ModelTag = "gemma4:e2b",  # 高品質検証は gemma4:e4b を指定
    [string]$Endpoint = "http://localhost:11434",
    [string]$PromptFile = "prompts/plain.md",
    [double]$Temperature = 0.1,
    [int]$GpuLayers = 0  # 0=CPUのみ。VRAMが十分な環境では -1 を渡すと自動判定
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $InputTxt) { $InputTxt = Join-Path $repoRoot "samples/raw/sample_001_raw.txt" }

$promptPath = Join-Path $repoRoot $PromptFile
$dictPath = Join-Path $repoRoot "config/dictionary.json"
$outDir = Join-Path $repoRoot "samples/formatted"
$baseName = [IO.Path]::GetFileNameWithoutExtension($InputTxt) -replace "_raw$", ""
$outPath = Join-Path $outDir ($baseName + "_formatted.txt")

foreach ($required in @($promptPath, $InputTxt)) {
    if (-not (Test-Path $required)) { Write-Error "Not found: $required"; exit 1 }
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$rawText = (Get-Content -Raw -Encoding UTF8 $InputTxt).Trim()
$dictLines = ""
if (Test-Path $dictPath) {
    $dict = Get-Content -Raw -Encoding UTF8 $dictPath | ConvertFrom-Json
    $dictLines = ($dict.entries | ForEach-Object { "- " + ($_.patterns -join ", ") + " => " + $_.canonical }) -join "`n"
}

$prompt = (Get-Content -Raw -Encoding UTF8 $promptPath)
$prompt = $prompt.Replace("{{dictionary}}", $dictLines).Replace("{{raw_transcript}}", $rawText)

$options = @{ temperature = $Temperature }
if ($GpuLayers -ge 0) { $options.num_gpu = $GpuLayers }

$body = @{
    model    = $ModelTag
    messages = @(@{ role = "user"; content = $prompt })
    stream   = $false
    options  = $options
} | ConvertTo-Json -Depth 6

Write-Host "Calling $Endpoint/api/chat (model=$ModelTag) ..."
$sw = [Diagnostics.Stopwatch]::StartNew()
$response = Invoke-RestMethod -Uri "$Endpoint/api/chat" -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($body)) -ContentType "application/json" -TimeoutSec 300
$sw.Stop()

$formatted = $response.message.content.Trim()
if (-not $formatted) { Write-Error "Empty response from LLM"; exit 1 }

[IO.File]::WriteAllText($outPath, $formatted + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Host ""
Write-Host "Done in $($sw.Elapsed.TotalSeconds.ToString('F1'))s. Output: $outPath"
Write-Host "--- formatted ---"
Write-Host $formatted
