# FeatherScribe source archive script.
# Packs source code and documentation into a ZIP. Git history, the local runtime stack,
# build outputs, IDE/agent working files, large generated files such as models and audio,
# and local-only files ignored by .gitignore (docs/work, config/appsettings.local.json, ...)
# are excluded.
# Usage: powershell -ExecutionPolicy Bypass -File tools/archive_featherscribe.ps1
#        [-OutputDirectory path]
#
# -OutputDirectory defaults to $env:FEATHERSCRIBE_ARCHIVE_OUTPUT_DIR when it is set,
# otherwise to the "OutputPath\FeatherScribe" folder next to the repository.
# To use a machine-specific output directory without editing this script, set the
# environment variable in your local shell profile.
#
# Warning: every existing item in the output directory is deleted before the archive is
# created. Use a directory dedicated to archives.
#
# Keep this file ASCII-only: Windows PowerShell 5.1 reads BOM-less files as the ANSI code page.

param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
if (-not ("FeatherScribeArchivePath" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class FeatherScribeArchivePath
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle fileHandle,
        StringBuilder path,
        uint pathLength,
        uint flags);

    public static string GetFinalPath(string path)
    {
        using (SafeFileHandle handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            StringBuilder result = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(handle, result, (uint)result.Capacity, 0);
            if (length == 0 || length >= result.Capacity)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return result.ToString();
        }
    }
}
"@
}

function Get-CanonicalPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $existingPath = $fullPath
    $missingSegments = [Collections.Generic.Stack[string]]::new()
    while (-not (Test-Path -LiteralPath $existingPath)) {
        $parentPath = Split-Path -Parent $existingPath
        if (-not $parentPath -or $parentPath -eq $existingPath) {
            throw "Cannot resolve an existing ancestor for path: $fullPath"
        }

        $missingSegments.Push((Split-Path -Leaf $existingPath))
        $existingPath = $parentPath
    }

    $canonicalPath = [FeatherScribeArchivePath]::GetFinalPath($existingPath)
    if ($canonicalPath.StartsWith("\\?\UNC\", [StringComparison]::OrdinalIgnoreCase)) {
        $canonicalPath = "\\" + $canonicalPath.Substring(8)
    } elseif ($canonicalPath.StartsWith("\\?\", [StringComparison]::OrdinalIgnoreCase)) {
        $canonicalPath = $canonicalPath.Substring(4)
    }

    while ($missingSegments.Count -gt 0) {
        $canonicalPath = Join-Path $canonicalPath $missingSegments.Pop()
    }

    return [IO.Path]::GetFullPath($canonicalPath).TrimEnd('\', '/')
}

$repoRoot = Get-CanonicalPath (Split-Path -Parent $PSScriptRoot)
$repoRootWithSeparator = $repoRoot + [IO.Path]::DirectorySeparatorChar

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    if (-not [string]::IsNullOrWhiteSpace($env:FEATHERSCRIBE_ARCHIVE_OUTPUT_DIR)) {
        $OutputDirectory = $env:FEATHERSCRIBE_ARCHIVE_OUTPUT_DIR
    } else {
        $OutputDirectory = Join-Path (Split-Path -Parent $repoRoot) "OutputPath\FeatherScribe"
    }
}

$requestedOutputDirectoryPath = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
$pathToInspect = $requestedOutputDirectoryPath
while ($pathToInspect) {
    if (Test-Path -LiteralPath $pathToInspect) {
        $pathItem = Get-Item -LiteralPath $pathToInspect -Force
        if (($pathItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Output directory paths must not contain reparse points: $pathToInspect"
        }
    }

    $parentPath = Split-Path -Parent $pathToInspect
    if (-not $parentPath -or $parentPath -eq $pathToInspect) {
        break
    }

    $pathToInspect = $parentPath
}

$outputDirectoryPath = Get-CanonicalPath $requestedOutputDirectoryPath
$outputDirectoryWithSeparator = $outputDirectoryPath + [IO.Path]::DirectorySeparatorChar
$fileSystemRoot = [IO.Path]::GetPathRoot($outputDirectoryPath).TrimEnd('\', '/')

if ($outputDirectoryPath -eq $fileSystemRoot -or
    $outputDirectoryPath.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $repoRoot.StartsWith($outputDirectoryWithSeparator, [StringComparison]::OrdinalIgnoreCase) -or
    $outputDirectoryPath.StartsWith($repoRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe output directory: $outputDirectoryPath"
}

# Directory names excluded at any depth.
$excludeDirectoryNames = @(
    ".git",
    ".vs",
    ".idea",
    ".claude",
    ".agents",
    "bin",
    "obj",
    "local",
    "models",
    "debug_artifacts",
    "logs",
    "reports",
    "artifacts"
)
# Local-only items (ignored by .gitignore), relative to the repository root.
$excludeRelativePaths = @(
    "docs/work",
    "config/appsettings.local.json"
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

function Get-RelativeEntryName {
    param(
        [Parameter(Mandatory)]
        [string]$FullName
    )

    return $FullName.Substring($repoRootWithSeparator.Length) -replace '\\', '/'
}

function Get-ArchiveItems {
    param(
        [Parameter(Mandatory)]
        [string]$DirectoryPath
    )

    foreach ($child in Get-ChildItem -LiteralPath $DirectoryPath -Force) {
        $relativeName = Get-RelativeEntryName $child.FullName
        if ($excludeRelativePaths -contains $relativeName) {
            continue
        }

        if ($child.PSIsContainer) {
            if ($excludeDirectoryNames -contains $child.Name) {
                continue
            }

            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Archive inputs must not contain reparse points: $($child.FullName)"
            }

            Get-ArchiveItems $child.FullName
            continue
        }

        $isExcludedFile = $false
        foreach ($pattern in $excludeFilePatterns) {
            if ($child.Name -like $pattern) {
                $isExcludedFile = $true
                break
            }
        }

        if ($isExcludedFile) {
            continue
        }

        if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Archive inputs must not contain reparse points: $($child.FullName)"
        }

        $child
    }
}

$items = @(Get-ArchiveItems $repoRoot)
if ($items.Count -eq 0) {
    throw "No files found to archive."
}

if (Test-Path -LiteralPath $outputDirectoryPath) {
    foreach ($child in Get-ChildItem -LiteralPath $outputDirectoryPath -Force) {
        $childPath = [IO.Path]::GetFullPath($child.FullName)
        if (-not $childPath.StartsWith($outputDirectoryWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove an item outside the output directory: $childPath"
        }

        Remove-Item -LiteralPath $childPath -Recurse -Force
    }
} else {
    New-Item -ItemType Directory -Path $outputDirectoryPath -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$archivePath = Join-Path $outputDirectoryPath "FeatherScribe-$timestamp.zip"

Write-Host "Creating archive: $archivePath"
Write-Host "Excluded directory names: $($excludeDirectoryNames -join ', ')"
Write-Host "Excluded local paths: $($excludeRelativePaths -join ', ')"
Write-Host "Excluded file patterns: $($excludeFilePatterns -join ', ')"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = $null
try {
    $archive = [System.IO.Compression.ZipFile]::Open(
        $archivePath,
        [System.IO.Compression.ZipArchiveMode]::Create
    )
    foreach ($item in $items) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $item.FullName,
            (Get-RelativeEntryName $item.FullName),
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} catch {
    $archiveFailure = $_
    if ($null -ne $archive) {
        $archive.Dispose()
        $archive = $null
    }
    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    throw $archiveFailure
} finally {
    if ($null -ne $archive) {
        $archive.Dispose()
    }
}

Write-Host "Done. Archived $($items.Count) files to: $archivePath"
