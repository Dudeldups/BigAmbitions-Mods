[CmdletBinding()]
param(
    [string]$ManagedDoomRef = "9365696eb44326a3aab72c4bab217f7db8a87c96",
    [string]$MeltySynthRef = "17825ce95e27295ca0c084dd51dcd73d9da93531",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ModRoot = Split-Path -Parent $PSScriptRoot
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MCG_Doom_ThirdPartyPrepare_" + [Guid]::NewGuid().ToString("N"))
$ManagedDestination = Join-Path $ModRoot "Scripts\ThirdParty\ManagedDoom"
$MeltyDestination = Join-Path $ModRoot "Scripts\ThirdParty\MeltySynth"
$LicenseDestination = Join-Path $ModRoot "ThirdParty\Licenses"
$RuntimeLegalDestination = Join-Path $ModRoot "Config\Doom\Legal"
$WadDestination = Join-Path $ModRoot "Config\Doom\doom1.wad"
$SoundFontDestination = Join-Path $ModRoot "Config\Doom\Audio\TimGM6mb.sf2"
$RecordPath = Join-Path $ModRoot "THIRD_PARTY_PREPARED.txt"
$ManagedLicenseDestination = Join-Path $LicenseDestination "LICENSE_ManagedDoom.txt"
$MeltyLicenseDestination = Join-Path $LicenseDestination "LICENSE_MeltySynth.txt"
$DoomCopyrightDestination = Join-Path $RuntimeLegalDestination "DOOM_SHAREWARE_DEBIAN_COPYRIGHT.txt"
$SoundFontCopyrightDestination = Join-Path $RuntimeLegalDestination "TimGM6mb_DEBIAN_COPYRIGHT.txt"

$DoomArchiveUrl = "https://deb.debian.org/debian/pool/non-free/d/doom-wad-shareware/doom-wad-shareware_1.9.fixed.orig.tar.gz"
$DoomArchiveMd5 = "B1D0B2E814366FE926EA2773CA404137"
$DoomCopyrightUrl = "https://sources.debian.org/data/non-free/d/doom-wad-shareware/1.9.fixed-5/debian/copyright"
$SoundFontArchiveUrl = "https://ftp.debian.org/debian/pool/main/t/timgm6mb-soundfont/timgm6mb-soundfont_1.3.orig.tar.gz"
$SoundFontArchiveSha256 = "AF8F3A00E416DFB262BCAA904A1C84DF04A51B72BBC1313AED012BC754BDF99B"
$SoundFontCopyrightUrl = "https://metadata.ftp-master.debian.org/changelogs/main/t/timgm6mb-soundfont/timgm6mb-soundfont_1.3-5_copyright"

function Ensure-Directory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Download-File([string]$Uri, [string]$Destination) {
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Destination
}

function Resolve-GitHubCommit([string]$Repository, [string]$Ref) {
    $headers = @{ "User-Agent" = "MCG_Doom-ThirdParty-Prep" }
    $uri = "https://api.github.com/repos/$Repository/commits/$Ref"
    $result = Invoke-RestMethod -Headers $headers -Uri $uri
    return [string]$result.sha
}

function Has-CSharpPayload([string]$Path) {
    return $null -ne (Get-ChildItem -LiteralPath $Path -Filter "*.cs" -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Copy-ZipSubtree([string]$ArchivePath, [string]$RepositorySubPath, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $normalizedSubPath = $RepositorySubPath.Replace("\", "/").Trim([char]"/")
    $needle = "/" + $normalizedSubPath + "/"
    $copied = 0
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)

    try {
        foreach ($entry in $archive.Entries) {
            $entryName = $entry.FullName.Replace("\", "/")
            $index = $entryName.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase)
            if ($index -lt 0) {
                continue
            }

            $relative = $entryName.Substring($index + $needle.Length)
            if ([string]::IsNullOrWhiteSpace($relative)) {
                continue
            }

            $target = Join-Path $Destination ($relative.Replace("/", "\"))
            if ($entryName.EndsWith("/")) {
                Ensure-Directory $target
                continue
            }

            Ensure-Directory (Split-Path -Parent $target)
            $input = $entry.Open()
            $output = [System.IO.File]::Open($target, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
            $copied++
        }
    }
    finally {
        $archive.Dispose()
    }

    if ($copied -eq 0) {
        throw "No files were found under '$RepositorySubPath' in archive '$ArchivePath'."
    }

    return $copied
}

function Copy-ZipRootFile([string]$ArchivePath, [string]$FileName, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entry = $archive.Entries | Where-Object {
            $name = $_.FullName.Replace("\", "/")
            $name -match ("^[^/]+/" + [regex]::Escape($FileName) + "$")
        } | Select-Object -First 1

        if ($null -eq $entry) {
            return $false
        }

        Ensure-Directory (Split-Path -Parent $Destination)
        $input = $entry.Open()
        $output = [System.IO.File]::Open($Destination, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $input.CopyTo($output)
        }
        finally {
            $output.Dispose()
            $input.Dispose()
        }
        return $true
    }
    finally {
        $archive.Dispose()
    }
}

if (Test-Path -LiteralPath $TempRoot) {
    Remove-Item -LiteralPath $TempRoot -Recurse -Force
}
Ensure-Directory $TempRoot
Ensure-Directory $ManagedDestination
Ensure-Directory $MeltyDestination
Ensure-Directory $LicenseDestination
Ensure-Directory $RuntimeLegalDestination
Ensure-Directory (Split-Path -Parent $WadDestination)
Ensure-Directory (Split-Path -Parent $SoundFontDestination)

$needManaged = $Force -or -not (Has-CSharpPayload $ManagedDestination)
$needWad = $Force -or -not (Test-Path -LiteralPath $WadDestination -PathType Leaf)
$needMelty = $Force -or -not (Has-CSharpPayload $MeltyDestination)
$needSoundFont = $Force -or -not (Test-Path -LiteralPath $SoundFontDestination -PathType Leaf)

$resolvedManagedCommit = $ManagedDoomRef
$managedZipSha256 = "existing"
$resolvedMeltyCommit = $MeltySynthRef
$meltyZipSha256 = "existing"
$archiveHash = "existing"
$soundFontArchiveHash = "existing"

if ($needManaged) {
    $resolvedManagedCommit = Resolve-GitHubCommit "sinshu/managed-doom" $ManagedDoomRef
    Write-Host "Managed Doom ref '$ManagedDoomRef' resolved to $resolvedManagedCommit"

    $managedZip = Join-Path $TempRoot "managed-doom.zip"
    $managedExtract = Join-Path $TempRoot "managed-doom"
    $managedUrl = "https://github.com/sinshu/managed-doom/archive/$resolvedManagedCommit.zip"
    Download-File $managedUrl $managedZip
    Expand-Archive -LiteralPath $managedZip -DestinationPath $managedExtract -Force

    $managedRepositoryRoot = Get-ChildItem -LiteralPath $managedExtract -Directory | Select-Object -First 1
    if ($null -eq $managedRepositoryRoot) {
        throw "Could not locate the extracted Managed Doom repository."
    }

    $managedSource = Join-Path $managedRepositoryRoot.FullName "ManagedDoom\src"
    if (-not (Test-Path -LiteralPath $managedSource)) {
        throw "Managed Doom source folder was not found at: $managedSource"
    }

    Get-ChildItem -LiteralPath $ManagedDestination -Force |
        Where-Object { $_.Name -ne "README.md" } |
        Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $managedSource "*") -Destination $ManagedDestination -Recurse -Force

    $silkPath = Join-Path $ManagedDestination "Silk"
    if (Test-Path -LiteralPath $silkPath) {
        Remove-Item -LiteralPath $silkPath -Recurse -Force
    }

    $forbiddenNamespaces = "^\s*using\s+(Silk\.NET|TrippyGL|DrippyAL|MeltySynth)"
    $forbiddenMatches = Get-ChildItem -LiteralPath $ManagedDestination -Filter "*.cs" -Recurse |
        Select-String -Pattern $forbiddenNamespaces -ErrorAction SilentlyContinue
    if ($forbiddenMatches) {
        $forbiddenMatches | Select-Object -First 20 | ForEach-Object { Write-Host $_.Path ":" $_.LineNumber " " $_.Line }
        throw "Managed Doom core still references a desktop/external dependency."
    }

    $managedLicenseSource = Join-Path $managedRepositoryRoot.FullName "licenses\LICENSE_ManagedDoom.txt"
    if (Test-Path -LiteralPath $managedLicenseSource) {
        Copy-Item -LiteralPath $managedLicenseSource -Destination $ManagedLicenseDestination -Force
        Copy-Item -LiteralPath $managedLicenseSource -Destination (Join-Path $RuntimeLegalDestination "LICENSE_ManagedDoom.txt") -Force
    }

    $managedZipSha256 = (Get-FileHash -LiteralPath $managedZip -Algorithm SHA256).Hash.ToUpperInvariant()
}
else {
    Write-Host "Managed Doom payload already present; keeping existing source."
}

if ($needMelty) {
    $resolvedMeltyCommit = Resolve-GitHubCommit "sinshu/meltysynth" $MeltySynthRef
    Write-Host "MeltySynth ref '$MeltySynthRef' resolved to $resolvedMeltyCommit"

    $meltyZip = Join-Path $TempRoot "meltysynth.zip"
    $meltyUrl = "https://github.com/sinshu/meltysynth/archive/$resolvedMeltyCommit.zip"
    Download-File $meltyUrl $meltyZip

    # Do NOT Expand-Archive the complete repository here. MeltySynth's test
    # reference data contains very deep paths which exceed Windows
    # PowerShell 5 / .NET Framework path handling. We only vendor the small
    # MeltySynth/src subtree that the mod actually compiles.
    Get-ChildItem -LiteralPath $MeltyDestination -Force |
        Where-Object { $_.Name -ne "README.md" } |
        Remove-Item -Recurse -Force

    $copiedMeltyFiles = Copy-ZipSubtree $meltyZip "MeltySynth/src" $MeltyDestination
    Write-Host "Copied $copiedMeltyFiles MeltySynth source file(s) without extracting test data."

    $meltyLicenseTemp = Join-Path $TempRoot "LICENSE_MeltySynth.txt"
    if (Copy-ZipRootFile $meltyZip "LICENSE.txt" $meltyLicenseTemp) {
        Copy-Item -LiteralPath $meltyLicenseTemp -Destination $MeltyLicenseDestination -Force
        Copy-Item -LiteralPath $meltyLicenseTemp -Destination (Join-Path $RuntimeLegalDestination "LICENSE_MeltySynth.txt") -Force
    }

    $meltyZipSha256 = (Get-FileHash -LiteralPath $meltyZip -Algorithm SHA256).Hash.ToUpperInvariant()
}
else {
    Write-Host "MeltySynth payload already present; keeping existing source."
}

if ($needWad) {
    $doomArchive = Join-Path $TempRoot "doom-wad-shareware_1.9.fixed.orig.tar.gz"
    $doomExtract = Join-Path $TempRoot "doom-shareware"
    Download-File $DoomArchiveUrl $doomArchive

    $archiveHash = (Get-FileHash -LiteralPath $doomArchive -Algorithm MD5).Hash.ToUpperInvariant()
    if ($archiveHash -ne $DoomArchiveMd5) {
        throw "Unexpected DOOM shareware archive MD5. Expected $DoomArchiveMd5, got $archiveHash."
    }

    Ensure-Directory $doomExtract
    & tar.exe -xzf $doomArchive -C $doomExtract
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed to extract the DOOM shareware archive."
    }

    $wadSource = Get-ChildItem -LiteralPath $doomExtract -Filter "doom1.wad" -File -Recurse | Select-Object -First 1
    if ($null -eq $wadSource) {
        throw "doom1.wad was not found in the extracted Debian shareware archive."
    }
    Copy-Item -LiteralPath $wadSource.FullName -Destination $WadDestination -Force
}
else {
    Write-Host "doom1.wad already present; keeping existing file."
}

if ($needSoundFont) {
    $soundFontArchive = Join-Path $TempRoot "timgm6mb-soundfont_1.3.orig.tar.gz"
    $soundFontExtract = Join-Path $TempRoot "timgm6mb"
    Download-File $SoundFontArchiveUrl $soundFontArchive

    $soundFontArchiveHash = (Get-FileHash -LiteralPath $soundFontArchive -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($soundFontArchiveHash -ne $SoundFontArchiveSha256) {
        throw "Unexpected TimGM6mb archive SHA256. Expected $SoundFontArchiveSha256, got $soundFontArchiveHash."
    }

    Ensure-Directory $soundFontExtract
    & tar.exe -xzf $soundFontArchive -C $soundFontExtract
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed to extract the TimGM6mb archive."
    }

    $soundFontSource = Get-ChildItem -LiteralPath $soundFontExtract -Filter "TimGM6mb.sf2" -File -Recurse | Select-Object -First 1
    if ($null -eq $soundFontSource) {
        throw "TimGM6mb.sf2 was not found in the extracted Debian source archive."
    }
    Copy-Item -LiteralPath $soundFontSource.FullName -Destination $SoundFontDestination -Force
}
else {
    Write-Host "TimGM6mb.sf2 already present; keeping existing file."
}

# Keep release/legal metadata complete even when the actual third-party payload
# was already prepared in an earlier run. These are deliberately small
# downloads and are copied into Config so the external build ships them.
if ($Force -or -not (Test-Path -LiteralPath $ManagedLicenseDestination -PathType Leaf)) {
    $managedLicenseUrl = "https://raw.githubusercontent.com/sinshu/managed-doom/$ManagedDoomRef/licenses/LICENSE_ManagedDoom.txt"
    Download-File $managedLicenseUrl $ManagedLicenseDestination
}
if ($Force -or -not (Test-Path -LiteralPath $MeltyLicenseDestination -PathType Leaf)) {
    $meltyLicenseUrl = "https://raw.githubusercontent.com/sinshu/meltysynth/$MeltySynthRef/LICENSE.txt"
    Download-File $meltyLicenseUrl $MeltyLicenseDestination
}
if ($Force -or -not (Test-Path -LiteralPath $DoomCopyrightDestination -PathType Leaf)) {
    Download-File $DoomCopyrightUrl $DoomCopyrightDestination
}
if ($Force -or -not (Test-Path -LiteralPath $SoundFontCopyrightDestination -PathType Leaf)) {
    Download-File $SoundFontCopyrightUrl $SoundFontCopyrightDestination
}

Copy-Item -LiteralPath $ManagedLicenseDestination -Destination (Join-Path $RuntimeLegalDestination "LICENSE_ManagedDoom.txt") -Force
Copy-Item -LiteralPath $MeltyLicenseDestination -Destination (Join-Path $RuntimeLegalDestination "LICENSE_MeltySynth.txt") -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "LICENSE") -Destination (Join-Path $RuntimeLegalDestination "GPL-2.0.txt") -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "LICENSE") -Destination (Join-Path $RuntimeLegalDestination "MCG_Doom_GPL-2.0.txt") -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "DOOM_SHAREWARE_NOTICE.md") -Destination $RuntimeLegalDestination -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "THIRD_PARTY_NOTICES.md") -Destination $RuntimeLegalDestination -Force
Copy-Item -LiteralPath (Join-Path $ModRoot "ThirdParty\MODIFICATIONS.md") -Destination (Join-Path $RuntimeLegalDestination "THIRD_PARTY_MODIFICATIONS.md") -Force

$managedCompatibility = Join-Path $PSScriptRoot "ApplyManagedDoomCompatibility.ps1"
$meltyCompatibility = Join-Path $PSScriptRoot "ApplyMeltySynthCompatibility.ps1"
if (-not (Test-Path -LiteralPath $managedCompatibility -PathType Leaf)) {
    throw "Managed Doom compatibility patch script was not found: $managedCompatibility"
}
if (-not (Test-Path -LiteralPath $meltyCompatibility -PathType Leaf)) {
    throw "MeltySynth compatibility patch script was not found: $meltyCompatibility"
}
& $managedCompatibility
& $meltyCompatibility

$wadSha256 = (Get-FileHash -LiteralPath $WadDestination -Algorithm SHA256).Hash.ToUpperInvariant()
$soundFontSha256 = (Get-FileHash -LiteralPath $SoundFontDestination -Algorithm SHA256).Hash.ToUpperInvariant()

$record = @"
MCG_Doom third-party preparation
GeneratedUtc: $([DateTime]::UtcNow.ToString("o"))
ManagedDoomRequestedRef: $ManagedDoomRef
ManagedDoomResolvedCommit: $resolvedManagedCommit
ManagedDoomArchiveSha256: $managedZipSha256
MeltySynthRequestedRef: $MeltySynthRef
MeltySynthResolvedCommit: $resolvedMeltyCommit
MeltySynthArchiveSha256: $meltyZipSha256
DoomSharewareArchiveUrl: $DoomArchiveUrl
DoomSharewareArchiveMd5: $archiveHash
Doom1WadSha256: $wadSha256
TimGM6mbArchiveUrl: $SoundFontArchiveUrl
TimGM6mbArchiveSha256: $soundFontArchiveHash
TimGM6mbSf2Sha256: $soundFontSha256
"@
Set-Content -LiteralPath $RecordPath -Value $record -Encoding UTF8

Remove-Item -LiteralPath $TempRoot -Recurse -Force

Write-Host ""
Write-Host "Third-party payload prepared successfully." -ForegroundColor Green
Write-Host "Managed Doom: $resolvedManagedCommit"
Write-Host "MeltySynth:  $resolvedMeltyCommit"
Write-Host "DOOM1.WAD SHA256: $wadSha256"
Write-Host "TimGM6mb SHA256:  $soundFontSha256"
Write-Host "Next: .\tools\BuildAndInstall.ps1"
