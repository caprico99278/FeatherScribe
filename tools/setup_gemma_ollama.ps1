# Gemma 4 + Ollama setup script.
# Extracts the official Ollama zip to local/ollama when Ollama is not installed,
# then pulls the Gemma 4 model.
# Usage: powershell -ExecutionPolicy Bypass -File tools/setup_gemma_ollama.ps1 [-ModelTag gemma4:e4b]
#
# Keep this file ASCII-only: Windows PowerShell 5.1 reads BOM-less files as the ANSI code page.

param(
    [string]$ModelTag = "gemma4:e2b",  # Lightweight default. Pull gemma4:e4b additionally for the quality mode.
    [string]$OllamaVersion = "v0.31.1"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDir = Join-Path $repoRoot "local"
$ollamaDir = Join-Path $localDir "ollama"

# 1. Resolve ollama.exe (PATH > local/ollama > download)
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

# 2. Start the server when it is not running
try {
    Invoke-RestMethod -Uri "http://localhost:11434/api/version" -TimeoutSec 3 | Out-Null
    Write-Host "Ollama server is already running."
} catch {
    Write-Host "Starting ollama serve ..."
    Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WindowStyle Hidden
    Start-Sleep -Seconds 5
}

# 3. Pull the Gemma 4 model (e4b is approx. 9.6GB)
Write-Host "Pulling $ModelTag ..."
& $ollamaExe pull $ModelTag

Write-Host ""
Write-Host "Setup complete. Model: $ModelTag"
Write-Host "Check that llm.model in config/appsettings.json matches this model."
