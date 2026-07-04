# Gemma 4 + Ollama セットアップスクリプト
# Ollama が未導入なら公式 zip を local/ollama へ展開し、Gemma 4 モデルを pull する。
# 使い方: powershell -ExecutionPolicy Bypass -File tools/setup_gemma_ollama.ps1 [-ModelTag gemma4:e4b]

param(
    [string]$ModelTag = "gemma4:e2b",  # 既定は軽量モデル。高品質モード用は gemma4:e4b を追加でpull
    [string]$OllamaVersion = "v0.31.1"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDir = Join-Path $repoRoot "local"
$ollamaDir = Join-Path $localDir "ollama"

# 1. ollama.exe の場所を決める (PATH 上 > local/ollama > ダウンロード)
$ollamaExe = (Get-Command ollama -ErrorAction SilentlyContinue).Source
if (-not $ollamaExe) {
    $ollamaExe = Join-Path $ollamaDir "ollama.exe"
    if (-not (Test-Path $ollamaExe)) {
        Write-Host "Downloading Ollama $OllamaVersion (approx. 1.5GB) ..."
        New-Item -ItemType Directory -Force -Path $ollamaDir | Out-Null
        $zipPath = Join-Path $localDir "ollama-windows-amd64.zip"
        $zipUrl = "https://github.com/ollama/ollama/releases/download/$OllamaVersion/ollama-windows-amd64.zip"
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
        Expand-Archive -Path $zipPath -DestinationPath $ollamaDir -Force
    }
}
Write-Host "Using ollama: $ollamaExe"

# 2. サーバーが起動していなければ起動
try {
    Invoke-RestMethod -Uri "http://localhost:11434/api/version" -TimeoutSec 3 | Out-Null
    Write-Host "Ollama server is already running."
} catch {
    Write-Host "Starting ollama serve ..."
    Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WindowStyle Hidden
    Start-Sleep -Seconds 5
}

# 3. Gemma 4 モデルの pull (e4b は約 9.6GB)
Write-Host "Pulling $ModelTag ..."
& $ollamaExe pull $ModelTag

Write-Host ""
Write-Host "Setup complete. Model: $ModelTag"
Write-Host "config/appsettings.json の llm.model と一致しているか確認してください。"
