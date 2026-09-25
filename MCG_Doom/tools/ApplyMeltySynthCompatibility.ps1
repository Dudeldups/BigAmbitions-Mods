[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ModRoot = Split-Path -Parent $PSScriptRoot
$MeltyRoot = Join-Path $ModRoot "Scripts\ThirdParty\MeltySynth"

if (-not (Test-Path -LiteralPath $MeltyRoot -PathType Container)) {
    throw "MeltySynth source directory is missing: $MeltyRoot"
}

$meltyFiles = @(Get-ChildItem -LiteralPath $MeltyRoot -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue)
if ($meltyFiles.Count -eq 0) {
    throw "MeltySynth source has not been prepared. Run .\tools\PrepareThirdParty.ps1 first."
}

# These helpers are only for standard MIDI-file playback / Span-based output.
# DOOM supplies MUS data and MCG_Doom owns the Unity audio output instead.
$unusedFiles = @(
    "AudioRendererEx.cs",
    "IAudioRenderer.cs",
    "MidiFile.cs",
    "MidiFileLoopType.cs",
    "MidiFileSequencer.cs"
)
foreach ($name in $unusedFiles) {
    $path = Join-Path $MeltyRoot $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}

$changedFiles = 0

function Set-PatchedContent([string]$Path, [string]$Original, [string]$Patched) {
    if ($Patched -ceq $Original) {
        return
    }

    Set-Content -LiteralPath $Path -Value $Patched -Encoding UTF8
    $script:changedFiles++
}

$meltyFiles = @(Get-ChildItem -LiteralPath $MeltyRoot -Filter "*.cs" -File -Recurse)
foreach ($file in $meltyFiles) {
    $original = Get-Content -LiteralPath $file.FullName -Raw
    $text = $original

    # .NET Framework 4.7.2 has no System.MathF and no Math.Clamp.
    $text = $text.Replace("MathF.", "MCG_Doom.Compatibility.ManagedDoomNet472Compat.")
    $text = $text.Replace("Math.Clamp(", "MCG_Doom.Compatibility.ManagedDoomNet472Compat.Clamp(")

    Set-PatchedContent $file.FullName $original $text
}

# MeltySynth's vectorized helper relies on MemoryMarshal/Span. The scalar loop
# is functionally equivalent and easily fast enough for the tiny DOOM music mix.
$arrayMathPath = Join-Path $MeltyRoot "ArrayMath.cs"
if (Test-Path -LiteralPath $arrayMathPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $arrayMathPath -Raw
    $text = $original
    $pattern = '(?s)        public static void MultiplyAdd\(float a, float\[\] x, float\[\] destination\)\s*\{.*?\n        \}\s*\n        public static void MultiplyAdd\(float a, float step'
    $replacement = @'
        public static void MultiplyAdd(float a, float[] x, float[] destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] += a * x[i];
            }
        }
        public static void MultiplyAdd(float a, float step
'@
    $text = [regex]::Replace($text, $pattern, $replacement)
    $text = $text.Replace("using System.Numerics;`r`n", "")
    $text = $text.Replace("using System.Numerics;`n", "")
    $text = $text.Replace("using System.Runtime.InteropServices;`r`n", "")
    $text = $text.Replace("using System.Runtime.InteropServices;`n", "")
    Set-PatchedContent $arrayMathPath $original $text
}

# The bundled TimGM6mb is an ordinary little-endian SF2. Read its PCM chunk
# through BinaryReader + Buffer.BlockCopy instead of MemoryMarshal/Span.
$sampleDataPath = Join-Path $MeltyRoot "SoundFontSampleData.cs"
if (Test-Path -LiteralPath $sampleDataPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $sampleDataPath -Raw
    $text = $original
    $text = $text.Replace(
        "                        reader.Read(MemoryMarshal.Cast<short, byte>(samples));",
        @'
                        var sampleBytes = reader.ReadBytes(size);
                        if (sampleBytes.Length != size)
                        {
                            throw new EndOfStreamException("Unexpected end of SoundFont sample data.");
                        }
                        Buffer.BlockCopy(sampleBytes, 0, samples, 0, size);
'@)

    $oggPattern = '(?s)            if \(Encoding\.ASCII\.GetString\(MemoryMarshal\.Cast<short, byte>\(samples\)\.Slice\(0, 4\)\) == "OggS"\)\s*\{\s*throw new NotSupportedException\("SoundFont3 is not yet supported\."\);\s*\}\s*'
    $text = [regex]::Replace($text, $oggPattern, "")
    $text = $text.Replace("using System.Runtime.InteropServices;`r`n", "")
    $text = $text.Replace("using System.Runtime.InteropServices;`n", "")
    Set-PatchedContent $sampleDataPath $original $text
}

# This public convenience view is not required by the synthesizer. Expose the
# same data through IReadOnlyList so net472 does not need ReadOnlySpan.
$soundFontPath = Join-Path $MeltyRoot "SoundFont.cs"
if (Test-Path -LiteralPath $soundFontPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $soundFontPath -Raw
    $text = $original.Replace(
        "public ReadOnlySpan<short> WaveData => waveData;",
        "public IReadOnlyList<short> WaveData => waveData;")
    Set-PatchedContent $soundFontPath $original $text
}

# Replace the Span-based renderer with an array + offset version. This is the
# only rendering shape MCG_Doom's streaming AudioClip needs.
$synthPath = Join-Path $MeltyRoot "Synthesizer.cs"
if (Test-Path -LiteralPath $synthPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $synthPath -Raw
    $text = $original.Replace(
        "public sealed class Synthesizer : IAudioRenderer",
        "public sealed class Synthesizer")

    $pattern = '(?s)        public void Render\(Span<float> left, Span<float> right\)\s*\{.*?\n        \}\s*\n\s*        private void RenderBlock\(\)'
    $replacement = @'
        public void Render(float[] left, int leftOffset, float[] right, int rightOffset, int sampleCount)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (sampleCount < 0 || leftOffset < 0 || rightOffset < 0 ||
                leftOffset + sampleCount > left.Length || rightOffset + sampleCount > right.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            }

            var wrote = 0;
            while (wrote < sampleCount)
            {
                if (blockRead == blockSize)
                {
                    RenderBlock();
                    blockRead = 0;
                }

                var srcRem = blockSize - blockRead;
                var dstRem = sampleCount - wrote;
                var rem = Math.Min(srcRem, dstRem);
                Array.Copy(blockLeft, blockRead, left, leftOffset + wrote, rem);
                Array.Copy(blockRight, blockRead, right, rightOffset + wrote, rem);

                blockRead += rem;
                wrote += rem;
            }
        }

        public void Render(float[] left, float[] right)
        {
            if (left == null) throw new ArgumentNullException(nameof(left));
            if (right == null) throw new ArgumentNullException(nameof(right));
            if (left.Length != right.Length)
            {
                throw new ArgumentException("The output buffers for the left and right must be the same length.");
            }

            Render(left, 0, right, 0, left.Length);
        }

        private void RenderBlock()
'@
    $text = [regex]::Replace($text, $pattern, $replacement)
    Set-PatchedContent $synthPath $original $text
}


# SoundFont instrument/preset zone slicing uses Span<Zone> upstream. Replace
# it with explicit array start/count parameters so the parser stays allocation
# free without depending on System.Span<T> on .NET Framework 4.7.2.
$instrumentRegionPath = Join-Path $MeltyRoot "InstrumentRegion.cs"
if (Test-Path -LiteralPath $instrumentRegionPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $instrumentRegionPath -Raw
    $text = $original
    $pattern = '(?s)        internal static InstrumentRegion\[\] Create\(Instrument instrument, Span<Zone> zones, SampleHeader\[\] samples\)\s*\{.*?\n        \}\s*\n        private void SetParameter'
    $replacement = @'
        internal static InstrumentRegion[] Create(Instrument instrument, Zone[] zones, int zoneStart, int zoneCount, SampleHeader[] samples)
        {
            if (zones == null) throw new ArgumentNullException(nameof(zones));
            if (zoneStart < 0 || zoneCount <= 0 || zoneStart + zoneCount > zones.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(zoneCount));
            }

            // Is the first one the global zone?
            var first = zones[zoneStart];
            if (first.Generators.Count == 0 || first.Generators.Last().Type != GeneratorType.SampleID)
            {
                // The first one is the global zone.
                var global = first;
                // The global zone is regarded as the base setting of subsequent zones.
                var regions = new InstrumentRegion[zoneCount - 1];
                for (var i = 0; i < regions.Length; i++)
                {
                    regions[i] = new InstrumentRegion(instrument, global, zones[zoneStart + i + 1], samples);
                }
                return regions;
            }
            else
            {
                // No global zone.
                var regions = new InstrumentRegion[zoneCount];
                for (var i = 0; i < regions.Length; i++)
                {
                    regions[i] = new InstrumentRegion(instrument, Zone.Empty, zones[zoneStart + i], samples);
                }
                return regions;
            }
        }
        private void SetParameter
'@
    $text = [regex]::Replace($text, $pattern, $replacement)
    Set-PatchedContent $instrumentRegionPath $original $text
}

$presetRegionPath = Join-Path $MeltyRoot "PresetRegion.cs"
if (Test-Path -LiteralPath $presetRegionPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $presetRegionPath -Raw
    $text = $original
    $pattern = '(?s)        internal static PresetRegion\[\] Create\(Preset preset, Span<Zone> zones, Instrument\[\] instruments\)\s*\{.*?\n        \}\s*\n        private void SetParameter'
    $replacement = @'
        internal static PresetRegion[] Create(Preset preset, Zone[] zones, int zoneStart, int zoneCount, Instrument[] instruments)
        {
            if (zones == null) throw new ArgumentNullException(nameof(zones));
            if (zoneStart < 0 || zoneCount <= 0 || zoneStart + zoneCount > zones.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(zoneCount));
            }

            // Is the first one the global zone?
            var first = zones[zoneStart];
            if (first.Generators.Count == 0 || first.Generators.Last().Type != GeneratorType.Instrument)
            {
                // The first one is the global zone.
                var global = first;
                // The global zone is regarded as the base setting of subsequent zones.
                var regions = new PresetRegion[zoneCount - 1];
                for (var i = 0; i < regions.Length; i++)
                {
                    regions[i] = new PresetRegion(preset, global, zones[zoneStart + i + 1], instruments);
                }
                return regions;
            }
            else
            {
                // No global zone.
                var regions = new PresetRegion[zoneCount];
                for (var i = 0; i < regions.Length; i++)
                {
                    regions[i] = new PresetRegion(preset, Zone.Empty, zones[zoneStart + i], instruments);
                }
                return regions;
            }
        }
        private void SetParameter
'@
    $text = [regex]::Replace($text, $pattern, $replacement)
    Set-PatchedContent $presetRegionPath $original $text
}

$instrumentPath = Join-Path $MeltyRoot "Instrument.cs"
if (Test-Path -LiteralPath $instrumentPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $instrumentPath -Raw
    $text = $original
    $pattern = 'var zoneSpan = zones\.AsSpan\(info\.ZoneStartIndex, zoneCount\);\s*regions = InstrumentRegion\.Create\(this, zoneSpan, samples\);'
    $replacement = 'regions = InstrumentRegion.Create(this, zones, info.ZoneStartIndex, zoneCount, samples);'
    $text = [regex]::Replace($text, $pattern, $replacement)
    Set-PatchedContent $instrumentPath $original $text
}

# ArraySegment<T>.Empty is not available in the .NET Framework 4.7.2
# reference assemblies used by the external build. Do NOT use default(ArraySegment<T>)
# here: although Count is zero, enumerating a default segment throws
# InvalidOperationException. Zone.Empty is enumerated while SoundFont regions are
# built, so give it a real empty backing array instead.
$zonePath = Join-Path $MeltyRoot "Zone.cs"
if (Test-Path -LiteralPath $zonePath -PathType Leaf) {
    $original = Get-Content -LiteralPath $zonePath -Raw
    $text = $original.Replace(
        'ArraySegment<Generator>.Empty',
        'new ArraySegment<Generator>(new Generator[0])')
    $text = $text.Replace(
        'default(ArraySegment<Generator>)',
        'new ArraySegment<Generator>(new Generator[0])')
    Set-PatchedContent $zonePath $original $text
}

$presetPath = Join-Path $MeltyRoot "Preset.cs"
if (Test-Path -LiteralPath $presetPath -PathType Leaf) {
    $original = Get-Content -LiteralPath $presetPath -Raw
    $text = $original
    $pattern = 'var zoneSpan = zones\.AsSpan\(info\.ZoneStartIndex, zoneCount\);\s*regions = PresetRegion\.Create\(this, zoneSpan, instruments\);'
    $replacement = 'regions = PresetRegion.Create(this, zones, info.ZoneStartIndex, zoneCount, instruments);'
    $text = [regex]::Replace($text, $pattern, $replacement)
    Set-PatchedContent $presetPath $original $text
}

# Fail early if the subset we keep still depends on APIs that the net472 build
# cannot provide. This turns upstream changes into a clear prep/build error.
$unsupportedPatterns = @(
    '\bSpan<',
    '\bReadOnlySpan<',
    '\.AsSpan\(',
    '\bMemoryMarshal\.',
    '\bMathF\.',
    '\bMath\.Clamp\(',
    '\bIAudioRenderer\b',
    '\bArrayPool<',
    'ArraySegment<[^>]+>\.Empty'
)

$remaining = @()
foreach ($pattern in $unsupportedPatterns) {
    $remaining += @(Get-ChildItem -LiteralPath $MeltyRoot -Filter "*.cs" -File -Recurse |
        Select-String -Pattern $pattern -ErrorAction SilentlyContinue)
}

if ($remaining.Count -gt 0) {
    Write-Host "[MCG_Doom] Unsupported MeltySynth API usages remain:" -ForegroundColor Red
    $remaining | Select-Object -First 30 | ForEach-Object {
        Write-Host ("  {0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
    }
    throw "MeltySynth .NET 4.7.2 compatibility patch is incomplete."
}

if ($changedFiles -gt 0) {
    Write-Host "[MCG_Doom] MeltySynth .NET 4.7.2 compatibility applied to $changedFiles file(s)." -ForegroundColor Green
}
else {
    Write-Host "[MCG_Doom] MeltySynth .NET 4.7.2 compatibility already applied."
}
