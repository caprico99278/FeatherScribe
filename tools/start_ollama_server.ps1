# Starts the local Ollama server for FeatherScribe.
# Keep this PowerShell window open while using LLM formatting.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$ollamaDir = Join-Path $repoRoot "local\ollama"
$ollamaExe = Join-Path $ollamaDir "ollama.exe"
$modelsDir = Join-Path $repoRoot "local\ollama-models"

if (-not (Test-Path $ollamaExe)) {
    throw "Ollama executable not found: $ollamaExe"
}

if (-not (Test-Path $modelsDir)) {
    throw "Ollama model directory not found: $modelsDir"
}

$env:OLLAMA_MODELS = (Resolve-Path $modelsDir).Path

Write-Host "OLLAMA_MODELS=$env:OLLAMA_MODELS"
Write-Host "Starting Ollama server on http://localhost:11434"
Write-Host "Keep this window open while FeatherScribe is running."

Push-Location $ollamaDir
try {
    & $ollamaExe serve
} finally {
    Pop-Location
}
