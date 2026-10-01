# Gemma 4 + Ollama setup script.
# Extracts the official Ollama zip to local/ollama when Ollama is not installed,
# then pulls the Gemma 4 model.
# Usage: powershell -ExecutionPolicy Bypass -File tools/setup_gemma_ollama.ps1 [-ModelTag gemma4:e4b]
#
# When the repo-local ollama (local/ollama) is used, models are stored in local/ollama-models,
# the same folder tools/start_ollama_server.ps1 serves from.
# When ollama is found on PATH (system install), that install's model folder is used.
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
$modelsDir = Join-Path $localDir "ollama-models"
$baseUrl = "http://localhost:11434"

# 1. Resolve ollama.exe (PATH > local/ollama > download)
$ollamaExe = (Get-Command ollama -ErrorAction SilentlyContinue).Source
$usingLocalOllama = $false
if (-not $ollamaExe) {
    $usingLocalOllama = $true
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

# 2. Choose the model folder. This must happen before the server is started,
#    because the server reads OLLAMA_MODELS at startup.
if ($usingLocalOllama) {
    New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null
    $env:OLLAMA_MODELS = (Resolve-Path $modelsDir).Path
    Write-Host "OLLAMA_MODELS=$env:OLLAMA_MODELS (same folder as tools/start_ollama_server.ps1)"
} else {
    Write-Host "Ollama was found on PATH; the system install's model folder is used (OLLAMA_MODELS is not changed)."
}

# 3. Start the server when it is not running
$serverRunning = $false
try {
    Invoke-RestMethod -Uri "$baseUrl/api/version" -TimeoutSec 3 | Out-Null
    $serverRunning = $true
} catch {
    $serverRunning = $false
}

if ($serverRunning) {
    Write-Host "Ollama server is already running."
    Write-Host "WARNING: A running Ollama server was found; the model is stored where that server keeps its models."
} else {
    Write-Host "Starting ollama serve ..."
    Start-Process -FilePath $ollamaExe -ArgumentList "serve" -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds(30)
    while (-not $serverRunning -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        try {
            Invoke-RestMethod -Uri "$baseUrl/api/version" -TimeoutSec 3 | Out-Null
            $serverRunning = $true
        } catch {
            $serverRunning = $false
        }
    }
    if (-not $serverRunning) {
        Write-Host "Ollama server did not answer within 30 seconds; trying to continue."
    }
    Write-Host "This script started 'ollama serve' as a hidden background process and leaves it running."
    Write-Host "Stop it from Task Manager (ollama.exe) when no longer needed,"
    Write-Host "or use tools/start_ollama_server.ps1 later to run the server in a window you can close."
}

# 4. Pull the Gemma 4 model (e4b is approx. 9.6GB) unless it is already present
$acceptedNames = @($ModelTag)
if (-not $ModelTag.Contains(":")) {
    $acceptedNames += ($ModelTag + ":latest")
}

$modelExists = $false
$tagsQueried = $false
try {
    $tags = Invoke-RestMethod -Uri "$baseUrl/api/tags" -TimeoutSec 5
    $tagsQueried = $true
    foreach ($model in @($tags.models)) {
        if ($null -ne $model -and $acceptedNames -contains [string]$model.name) {
            $modelExists = $true
        }
    }
} catch {
    Write-Host "Could not list installed models ($($_.Exception.Message)); pulling anyway."
}

if ($modelExists) {
    Write-Host "Model already exists. Skipping pull. ($ModelTag)"
} else {
    if ($tagsQueried) {
        Write-Host "Model $ModelTag is not installed yet."
    }
    Write-Host "Pulling $ModelTag ..."
    & $ollamaExe pull $ModelTag
    if ($LASTEXITCODE -ne 0) {
        throw "ollama pull failed (exit code $LASTEXITCODE)."
    }
}

Write-Host ""
Write-Host "Setup complete. Model: $ModelTag"
Write-Host "Check that llm.model in config/appsettings.json matches this model."
