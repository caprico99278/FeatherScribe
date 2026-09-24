# Runs the local formatter model benchmark.
# Usage: powershell -ExecutionPolicy Bypass -File tools/benchmark_format_models.ps1 [-Models qwen3:0.6b,qwen3:1.7b]

param(
    [string]$Models = "qwen3:0.6b,gemma3:1b,qwen3:1.7b,hf.co/SakanaAI/TinySwallow-1.5B-Instruct-GGUF:Q5_K_M,qwen3:4b,llama3.2:3b,phi4-mini:3.8b,gemma3:4b,gemma4:e2b,gemma4:e4b",
    [string]$InputDir = "samples/format_benchmark/input",
    [string]$OutputDir = "reports",
    [int]$TimeoutFast = 30,
    [int]$TimeoutQuality = 120,
    [int]$TimeoutReference = 180,
    [string]$Endpoint = "http://localhost:11434"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    dotnet run --project src/FeatherScribe.Benchmarks -- `
        --models $Models `
        --input $InputDir `
        --output $OutputDir `
        --endpoint $Endpoint `
        --timeout-fast $TimeoutFast `
        --timeout-quality $TimeoutQuality `
        --timeout-reference $TimeoutReference
} finally {
    Pop-Location
}
