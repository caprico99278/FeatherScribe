# FeatherScribe source archive script.
# Archives source code and documentation while excluding history, local runtime,
# build outputs, IDE folders, generated artifacts, and large model files.
# Default output directory is ..\OutputPath from the repository root.
# Usage: powershell -ExecutionPolicy Bypass -File tools/archive_featherscribe.ps1 [-OutputPath path]

param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$repoName = Split-Path -Leaf $repoRoot
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$defaultArchiveName = "$repoName-$timestamp.zip"

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot "..\OutputPath\$defaultArchiveName"
} elseif ([IO.Path]::GetExtension($OutputPath) -eq "") {
    $OutputPath = Join-Path $OutputPath $defaultArchiveName
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDir = Split-Path -Parent $OutputPath
$excludeDirs = @(
    ".claude",
    ".git",
    ".vs",
    ".idea",
    "bin",
    "obj",
    "local",
    "models",
    "debug_artifacts",
    "logs"
)
$excludeFilePatterns = @(
    "*.user",
    "*.ilk",
    "*.pdb",
    "*.idb",
    "*.gguf",
    "*.bin",
    "*.onnx",
    "*.wav",
    "*.zip"
)
$repoRootWithSeparator = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

if (Test-Path $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$items = Get-ChildItem -LiteralPath $repoRoot -Recurse -Force | Where-Object {
    if ($_.PSIsContainer) { return $false }

    $relativePath = $_.FullName.Substring($repoRootWithSeparator.Length)
    $pathParts = $relativePath -split '[\\/]'
    if ($pathParts | Where-Object { $excludeDirs -contains $_ }) { return $false }
    if ($_.FullName -eq $OutputPath) { return $false }

    foreach ($pattern in $excludeFilePatterns) {
        if ($_.Name -like $pattern) { return $false }
    }

    return $true
}

if (-not $items) {
    Write-Error "No files found to archive."
    exit 1
}

Write-Host "Creating archive: $OutputPath"
Write-Host "Excluded directories: $($excludeDirs -join ', ')"
Write-Host "Excluded file patterns: $($excludeFilePatterns -join ', ')"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($OutputPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in $items) {
        $relativePath = $item.FullName.Substring($repoRootWithSeparator.Length)
        $entryName = $relativePath -replace '\\', '/'
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $item.FullName,
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} finally {
    $archive.Dispose()
}

Write-Host ""
Write-Host "Done. Output: $OutputPath"
