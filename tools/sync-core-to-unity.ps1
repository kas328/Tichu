<#
.SYNOPSIS
    One-way sync: core/ (canonical) -> Assets/_Project/ (Unity mirror)

.DESCRIPTION
    Copies .cs files from the canonical dotnet source into the Unity mirror folders.
    Never deletes .meta files. Excludes obj/ and bin/ directories.

    Some test-tree files are dotnet-only by design (long-running benches and weight
    trainers, plus the frozen OldAiAgent baseline they measure against). They are
    excluded from both sync and drift check -- see the Exclude entry on the tests
    mapping. The Unity test tree additionally holds Unity-only tests that have no
    canonical counterpart; those are ignored because the comparison walks the
    canonical side only.

    Runs on Windows PowerShell and on pwsh under Linux (CI calls -Check there).

.PARAMETER Check
    Compare mirror vs canonical and exit with non-zero if any drift is detected.
    Does not modify any files.
#>
param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

function Path([string[]]$parts) { [IO.Path]::Combine(@($root) + $parts) }

$mappings = @(
    @{
        Src     = Path @("core", "src", "Tichu.Core")
        Dest    = Path @("Assets", "_Project", "Core")
        Exclude = @()
    },
    @{
        Src     = Path @("core", "src", "Tichu.GameFlow")
        Dest    = Path @("Assets", "_Project", "GameFlow")
        Exclude = @()
    },
    @{
        Src     = Path @("core", "tests", "Tichu.Core.Tests")
        Dest    = Path @("Assets", "_Project", "Tests", "EditMode")
        # dotnet 전용 하니스 — 유니티 테스트 러너에 들어가면 안 된다.
        # 벤치는 수천~수만 라운드를 돌아 에디터 메인 스레드를 점유하고,
        # 트레이너는 가중치 파일을 생성하며, OldAiAgent 는 벤치 비교용 동결 사본이다.
        Exclude = @("*Bench.cs", "*Trainer.cs", "*TrainerTests.cs", "OldAiAgent.cs")
    }
)

function Get-CsFiles([string]$dir, [string[]]$exclude) {
    Get-ChildItem -Recurse -Path $dir -Filter "*.cs" |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        Where-Object {
            $name = $_.Name
            -not ($exclude | Where-Object { $name -like $_ })
        }
}

function Get-RelativePath($file, [string]$srcRoot) {
    $file.FullName.Substring($srcRoot.Length).TrimStart([char[]]@('\', '/'))
}

if ($Check) {
    $drifted = $false

    foreach ($m in $mappings) {
        foreach ($srcFile in (Get-CsFiles $m.Src $m.Exclude)) {
            $rel      = Get-RelativePath $srcFile $m.Src
            $destPath = Join-Path $m.Dest $rel

            if (-not (Test-Path $destPath)) {
                Write-Host "[MISSING] $destPath"
                $drifted = $true
                continue
            }

            $srcHash  = (Get-FileHash $srcFile.FullName -Algorithm SHA256).Hash
            $destHash = (Get-FileHash $destPath         -Algorithm SHA256).Hash
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
        foreach ($srcFile in (Get-CsFiles $m.Src $m.Exclude)) {
            $rel      = Get-RelativePath $srcFile $m.Src
            $destPath = Join-Path $m.Dest $rel
            $destDir  = Split-Path $destPath -Parent

            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }

            $needsCopy = $true
            if (Test-Path $destPath) {
                $srcHash  = (Get-FileHash $srcFile.FullName -Algorithm SHA256).Hash
                $destHash = (Get-FileHash $destPath         -Algorithm SHA256).Hash
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
