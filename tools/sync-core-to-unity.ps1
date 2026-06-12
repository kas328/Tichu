<#
.SYNOPSIS
    One-way sync: core/ (canonical) -> Assets/_Project/ (Unity mirror)

.DESCRIPTION
    Copies .cs files from the canonical dotnet source into the Unity mirror folders.
    Never deletes .meta files. Excludes obj/ and bin/ directories.

.PARAMETER Check
    Compare mirror vs canonical and exit with non-zero if any drift is detected.
    Does not modify any files.
#>
param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

$mappings = @(
    @{
        Src  = Join-Path $root "core\src\Tichu.Core"
        Dest = Join-Path $root "Assets\_Project\Core"
    },
    @{
        Src  = Join-Path $root "core\src\Tichu.GameFlow"
        Dest = Join-Path $root "Assets\_Project\GameFlow"
    },
    @{
        Src  = Join-Path $root "core\tests\Tichu.Core.Tests"
        Dest = Join-Path $root "Assets\_Project\Tests\EditMode"
    }
)

function Get-CsFiles([string]$dir) {
    Get-ChildItem -Recurse -Path $dir -Filter "*.cs" |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
}

if ($Check) {
    $drifted = $false

    foreach ($m in $mappings) {
        $srcFiles = Get-CsFiles $m.Src
        foreach ($srcFile in $srcFiles) {
            $rel     = $srcFile.FullName.Substring($m.Src.Length).TrimStart('\')
            $destPath = Join-Path $m.Dest $rel

            if (-not (Test-Path $destPath)) {
                Write-Host "[MISSING] $destPath"
                $drifted = $true
                continue
            }

            $srcHash  = (Get-FileHash $srcFile.FullName  -Algorithm SHA256).Hash
            $destHash = (Get-FileHash $destPath           -Algorithm SHA256).Hash
            if ($srcHash -ne $destHash) {
                Write-Host "[DRIFT]   $destPath"
                $drifted = $true
            }
        }
    }

    if ($drifted) {
        Write-Host "Drift detected. Run sync-core-to-unity.ps1 to update the mirror."
        exit 1
    }
    else {
        Write-Host "Mirror is in sync."
        exit 0
    }
}
else {
    foreach ($m in $mappings) {
        $srcFiles = Get-CsFiles $m.Src
        foreach ($srcFile in $srcFiles) {
            $rel      = $srcFile.FullName.Substring($m.Src.Length).TrimStart('\')
            $destPath = Join-Path $m.Dest $rel
            $destDir  = Split-Path $destPath -Parent

            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }

            $needsCopy = $true
            if (Test-Path $destPath) {
                $srcHash  = (Get-FileHash $srcFile.FullName -Algorithm SHA256).Hash
                $destHash = (Get-FileHash $destPath          -Algorithm SHA256).Hash
                if ($srcHash -eq $destHash) { $needsCopy = $false }
            }

            if ($needsCopy) {
                Copy-Item -Path $srcFile.FullName -Destination $destPath -Force
                Write-Host "[COPIED]  $rel"
            }
        }
    }

    Write-Host "Sync complete."
}
