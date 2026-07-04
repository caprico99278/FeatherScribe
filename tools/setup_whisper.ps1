# whisper.cpp セットアップスクリプト
# 公式リリースの Windows x64 バイナリと ggml モデルを local/ 配下へ配置する。
# 使い方: powershell -ExecutionPolicy Bypass -File tools/setup_whisper.ps1 [-ModelSize small]

param(
    [ValidateSet("tiny", "base", "small", "medium", "large-v3")]
    [string]$ModelSize = "small",
    [string]$WhisperVersion = "v1.9.1"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDir = Join-Path $repoRoot "local"
$whisperDir = Join-Path $localDir "whisper"
$modelsDir = Join-Path $localDir "models"

New-Item -ItemType Directory -Force -Path $whisperDir, $modelsDir | Out-Null

# 1. whisper.cpp バイナリ
$zipPath = Join-Path $localDir "whisper-bin-x64.zip"
$zipUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/$WhisperVersion/whisper-bin-x64.zip"
if (-not (Test-Path (Join-Path $whisperDir "Release/whisper-cli.exe"))) {
    Write-Host "Downloading whisper.cpp $WhisperVersion ..."
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $whisperDir -Force
} else {
    Write-Host "whisper-cli.exe already exists. Skipping download."
}

# 2. ggml モデル (Hugging Face: ggerganov/whisper.cpp)
$modelPath = Join-Path $modelsDir "ggml-$ModelSize.bin"
$modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$ModelSize.bin"
if (-not (Test-Path $modelPath)) {
    Write-Host "Downloading model ggml-$ModelSize.bin ..."
    Invoke-WebRequest -Uri $modelUrl -OutFile $modelPath
} else {
    Write-Host "Model already exists. Skipping download."
}

Write-Host ""
Write-Host "Setup complete."
Write-Host "  whisper-cli : $whisperDir\Release\whisper-cli.exe"
Write-Host "  model       : $modelPath"
Write-Host "config/appsettings.json の asr.whisperExecutablePath / asr.modelPath を確認してください。"
