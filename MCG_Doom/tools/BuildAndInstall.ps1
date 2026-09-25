[CmdletBinding()]
param(
    [string]$McgDll,
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ModRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $ModRoot "..\..\.."))
$ExternalBuild = Join-Path $RepoRoot "tools\external-build\BuildBigAmbitionsMods.ps1"
$GameDlls = Join-Path $RepoRoot "Assets\_BaDependencies\GameDlls"
$CompileReference = Join-Path $GameDlls "LIB_BaComputerGames.dll"

$BigAmbitionsSteamAppId = "1331550"
$McgWorkshopItemId = "3793604724"

function Get-DefaultModsLocalRoot {
    $local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $localLow = $local -replace "\\Local$", "\LocalLow"
    return Join-Path $localLow "Hovgaard Games\Big Ambitions\ModsLocal"
}

function Add-UniquePath([System.Collections.Generic.List[string]]$List, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    try {
        $fullPath = [IO.Path]::GetFullPath($Path)
    }
    catch {
        return
    }

    foreach ($existing in $List) {
        if ([string]::Equals($existing, $fullPath, [StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    $List.Add($fullPath)
}

function Get-SteamLibraryRoots {
    $roots = New-Object 'System.Collections.Generic.List[string]'

    $steamInstallCandidates = @()

    try {
        $steam = Get-ItemProperty -LiteralPath "HKCU:\Software\Valve\Steam" -ErrorAction Stop
        if ($steam.SteamPath) {
            $steamInstallCandidates += $steam.SteamPath
        }
    }
    catch {
    }

    try {
        $steam = Get-ItemProperty -LiteralPath "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -ErrorAction Stop
        if ($steam.InstallPath) {
            $steamInstallCandidates += $steam.InstallPath
        }
    }
    catch {
    }

    if (${env:ProgramFiles(x86)}) {
        $steamInstallCandidates += (Join-Path ${env:ProgramFiles(x86)} "Steam")
    }

    foreach ($steamRoot in $steamInstallCandidates) {
        if ([string]::IsNullOrWhiteSpace($steamRoot)) {
            continue
        }

        $steamRoot = $steamRoot -replace '/', '\'
        if (-not (Test-Path -LiteralPath $steamRoot -PathType Container)) {
            continue
        }

        Add-UniquePath $roots $steamRoot

        $libraryFolders = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
        if (-not (Test-Path -LiteralPath $libraryFolders -PathType Leaf)) {
            continue
        }

        foreach ($line in Get-Content -LiteralPath $libraryFolders -ErrorAction SilentlyContinue) {
            if ($line -match '^\s*"path"\s+"(.+)"\s*$') {
                $libraryPath = $Matches[1] -replace '\\\\', '\'
                Add-UniquePath $roots $libraryPath
            }
        }
    }

    return $roots.ToArray()
}

function Get-McgAssemblyInfo([string]$Path) {
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($Path)
    if ($assembly.Name -ne "LIB_BaComputerGames") {
        throw "Expected assembly LIB_BaComputerGames, got '$($assembly.Name)' from $Path"
    }

    return $assembly
}

function Resolve-McgDll([string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = (Resolve-Path -LiteralPath $ExplicitPath).Path
        return [PSCustomObject]@{
            Path = $resolved
            Source = "explicit -McgDll"
        }
    }

    # Prefer the actual Steam Workshop dependency used by normal players.
    foreach ($steamLibrary in Get-SteamLibraryRoots) {
        $candidate = Join-Path $steamLibrary "steamapps\workshop\content\$BigAmbitionsSteamAppId\$McgWorkshopItemId\LIB_BaComputerGames.dll"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [PSCustomObject]@{
                Path = (Resolve-Path -LiteralPath $candidate).Path
                Source = "Steam Workshop item $McgWorkshopItemId"
            }
        }
    }

    # Fall back to a locally installed development copy.
    $modsLocal = Get-DefaultModsLocalRoot
    $modsLocalCandidate = Join-Path $modsLocal "LIB_BA_MoreComputerGames\LIB_BaComputerGames.dll"
    if (Test-Path -LiteralPath $modsLocalCandidate -PathType Leaf) {
        return [PSCustomObject]@{
            Path = (Resolve-Path -LiteralPath $modsLocalCandidate).Path
            Source = "ModsLocal"
        }
    }

    if (Test-Path -LiteralPath $modsLocal -PathType Container) {
        $found = Get-ChildItem -LiteralPath $modsLocal -Recurse -File -Filter "LIB_BaComputerGames.dll" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $found) {
            return [PSCustomObject]@{
                Path = $found.FullName
                Source = "ModsLocal (recursive fallback)"
            }
        }
    }

    # Last development fallback for a currently open/imported Unity SDK project.
    $scriptAssembly = Join-Path $RepoRoot "Library\ScriptAssemblies\LIB_BaComputerGames.dll"
    if (Test-Path -LiteralPath $scriptAssembly -PathType Leaf) {
        return [PSCustomObject]@{
            Path = (Resolve-Path -LiteralPath $scriptAssembly).Path
            Source = "Unity Library/ScriptAssemblies fallback"
        }
    }

    throw @"
LIB_BaComputerGames.dll was not found.
Checked the Steam Workshop item $McgWorkshopItemId first, then ModsLocal and the Unity SDK cache.
You can also pass the DLL explicitly:
  .\tools\BuildAndInstall.ps1 -McgDll "C:\path\to\LIB_BaComputerGames.dll"
"@
}

if (-not (Test-Path -LiteralPath $ExternalBuild -PathType Leaf)) {
    throw "External build script not found: $ExternalBuild"
}
if (-not (Test-Path -LiteralPath $GameDlls -PathType Container)) {
    throw "Game DLL reference directory not found: $GameDlls"
}

$wad = Join-Path $ModRoot "Config\Doom\doom1.wad"
if (-not (Test-Path -LiteralPath $wad -PathType Leaf)) {
    throw "doom1.wad is missing. Run .\tools\PrepareThirdParty.ps1 first."
}

$mcg = Resolve-McgDll $McgDll
$resolvedMcg = $mcg.Path
$mcgAssembly = Get-McgAssemblyInfo $resolvedMcg

Write-Host "[MCG_Doom] MCG reference source: $($mcg.Source)"
Write-Host "[MCG_Doom] MCG reference version: $($mcgAssembly.Version)"
Write-Host "[MCG_Doom] MCG reference path: $resolvedMcg"

$copiedReference = $false
$backupReference = $null
try {
    if (Test-Path -LiteralPath $CompileReference -PathType Leaf) {
        $existingHash = (Get-FileHash -LiteralPath $CompileReference -Algorithm SHA256).Hash
        $mcgHash = (Get-FileHash -LiteralPath $resolvedMcg -Algorithm SHA256).Hash
        if ($existingHash -ne $mcgHash) {
            $backupReference = "$CompileReference.mcgdoom-backup"
            Copy-Item -LiteralPath $CompileReference -Destination $backupReference -Force
            Copy-Item -LiteralPath $resolvedMcg -Destination $CompileReference -Force
            $copiedReference = $true
        }
    }
    else {
        Copy-Item -LiteralPath $resolvedMcg -Destination $CompileReference -Force
        $copiedReference = $true
    }

    Write-Host "[MCG_Doom] Compile-only MCG reference: $resolvedMcg"
    Write-Host "[MCG_Doom] Building through the normal SDK external builder..."

    $externalArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $ExternalBuild,
        "-ModName", "MCG_Doom"
    )
    if (-not $NoInstall) {
        $externalArgs += "-Install"
    }

    & powershell.exe @externalArgs
    $externalExitCode = $LASTEXITCODE
    if ($externalExitCode -ne 0) {
        throw "MCG_Doom external build failed with exit code $externalExitCode."
    }
}
finally {
    if ($copiedReference) {
        if ($null -ne $backupReference -and (Test-Path -LiteralPath $backupReference -PathType Leaf)) {
            Remove-Item -LiteralPath $backupReference -Force -ErrorAction SilentlyContinue
        }
        Write-Host "[MCG_Doom] Retained the ignored MCG compile reference for Unity assembly resolution."
    }
}
