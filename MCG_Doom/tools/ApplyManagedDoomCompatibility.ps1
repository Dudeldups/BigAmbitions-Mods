[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ModRoot = Split-Path -Parent $PSScriptRoot
$ManagedRoot = Join-Path $ModRoot "Scripts\ThirdParty\ManagedDoom"
$InputPath = Join-Path $ModRoot "Scripts\Input\UnityDoomInput.cs"

if (-not (Test-Path -LiteralPath $ManagedRoot -PathType Container)) {
    throw "Managed Doom source directory is missing: $ManagedRoot"
}

$managedFiles = @(Get-ChildItem -LiteralPath $ManagedRoot -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
if ($managedFiles.Count -eq 0) {
    throw "Managed Doom source has not been prepared. Run .\tools\PrepareThirdParty.ps1 first."
}

$changedFiles = 0
$replacementCount = 0

function Set-PatchedContent([string]$Path, [string]$Original, [string]$Patched) {
    if ($Patched -ceq $Original) {
        return
    }

    Set-Content -LiteralPath $Path -Value $Patched -Encoding UTF8
    $script:changedFiles++
}

foreach ($file in $managedFiles) {
    $original = Get-Content -LiteralPath $file.FullName -Raw
    $text = $original

    # ExceptionDispatchInfo.Throw(Exception) is a newer API. Inside these catch
    # blocks a plain rethrow preserves the original exception and stack trace.
    $before = $text
    $text = $text.Replace("ExceptionDispatchInfo.Throw(e);", "throw;")
    if ($text -cne $before) { $replacementCount++ }

    # .NET Framework 4.7.2 does not have Math.Clamp / MathF.Round.
    $before = $text
    $text = $text.Replace("Math.Clamp(", "MCG_Doom.Compatibility.ManagedDoomNet472Compat.Clamp(")
    if ($text -cne $before) { $replacementCount++ }

    $before = $text
    $text = $text.Replace("MathF.Round(", "MCG_Doom.Compatibility.ManagedDoomNet472Compat.Round(")
    if ($text -cne $before) { $replacementCount++ }

    # The char + StringSplitOptions overload is newer than the target framework.
    $before = $text
    $text = $text.Replace(".Split('=', StringSplitOptions.", ".Split(new[] { '=' }, StringSplitOptions.")
    $text = $text.Replace(".Split(' ', StringSplitOptions.", ".Split(new[] { ' ' }, StringSplitOptions.")
    if ($text -cne $before) { $replacementCount++ }

    Set-PatchedContent $file.FullName $original $text
}

# MemoryMarshal.Cast + AsSpan is only used here to reinterpret the RGBA byte
# destination as uints. Write the same little-endian bytes explicitly instead.
$rendererPath = Join-Path $ManagedRoot "Video\Renderer.cs"
if (Test-Path -LiteralPath $rendererPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $rendererPath -Raw
    $text = $original

    $pattern = 'var p = MemoryMarshal\.Cast<byte, uint>\(destination\.AsSpan\(\)\);\s*for \(var i = 0; i < p\.Length; i\+\+\)\s*\{\s*p\[i\] = colors\[screenData\[i\]\];\s*\}'
    $replacement = @'
for (var i = 0; i < screenData.Length; i++)
            {
                var color = colors[screenData[i]];
                var offset = i * 4;
                destination[offset] = (byte)color;
                destination[offset + 1] = (byte)(color >> 8);
                destination[offset + 2] = (byte)(color >> 16);
                destination[offset + 3] = (byte)(color >> 24);
            }
'@
    $text = [regex]::Replace($text, $pattern, $replacement)

    # No longer needed after replacing MemoryMarshal.
    if ($text -notmatch 'MemoryMarshal') {
        $text = $text.Replace("using System.Runtime.InteropServices;`r`n", "")
        $text = $text.Replace("using System.Runtime.InteropServices;`n", "")
    }

    if ($text -cne $original) {
        $replacementCount++
        Set-PatchedContent $rendererPath $original $text
    }
}

# Unity also has EventType, so explicitly select Managed Doom's event enum.
if (Test-Path -LiteralPath $InputPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $InputPath -Raw
    $text = $original.Replace(
        "new DoomEvent(EventType.KeyDown, doomKey)",
        "new DoomEvent(ManagedDoom.EventType.KeyDown, doomKey)")
    $text = $text.Replace(
        "new DoomEvent(EventType.KeyUp, doomKey)",
        "new DoomEvent(ManagedDoom.EventType.KeyUp, doomKey)")

    if ($text -cne $original) {
        $replacementCount++
        Set-PatchedContent $InputPath $original $text
    }
}

# Fail early if one of the known incompatible APIs still remains.
$knownUnsupportedPatterns = @(
    'ExceptionDispatchInfo\.Throw\(e\)',
    '\bMath\.Clamp\(',
    '\bMathF\.Round\(',
    'MemoryMarshal\.Cast<byte, uint>',
    '\.AsSpan\(\)',
    "\.Split\('[= ]', StringSplitOptions\."
)

$remaining = @()
foreach ($pattern in $knownUnsupportedPatterns) {
    $remaining += @(Get-ChildItem -LiteralPath $ManagedRoot -Filter "*.cs" -File -Recurse |
        Select-String -Pattern $pattern -ErrorAction SilentlyContinue)
}

if ($remaining.Count -gt 0) {
    Write-Host "[MCG_Doom] Known unsupported Managed Doom API usages remain:" -ForegroundColor Red
    $remaining | Select-Object -First 20 | ForEach-Object {
        Write-Host ("  {0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
    }
    throw "Managed Doom compatibility patch is incomplete."
}

if ($changedFiles -gt 0) {
    Write-Host "[MCG_Doom] Managed Doom .NET 4.7.2 compatibility applied to $changedFiles file(s)." -ForegroundColor Green
}
else {
    Write-Host "[MCG_Doom] Managed Doom .NET 4.7.2 compatibility already applied."
}

$meltyRoot = Join-Path $ModRoot "Scripts\ThirdParty\MeltySynth"
$meltyPatch = Join-Path $PSScriptRoot "ApplyMeltySynthCompatibility.ps1"
if ((Get-ChildItem -LiteralPath $meltyRoot -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue) -and
    (Test-Path -LiteralPath $meltyPatch -PathType Leaf)) {
    & $meltyPatch
}
