# Third-party source modifications

MCG_Doom 1.0.0 compiles its third-party engine/audio sources into the mod assembly for the Big Ambitions SDK's effective .NET Framework 4.7.2 target.

## Managed Doom

Upstream revision: `9365696eb44326a3aab72c4bab217f7db8a87c96`

The `Silk` desktop frontend is omitted. `tools/ApplyManagedDoomCompatibility.ps1` applies the compatibility changes required by the older target framework, including replacements for newer BCL APIs used by the upstream source. No gameplay, rendering, map, WAD, demo, music-selection, or sound-selection behavior is intentionally changed by those compatibility edits.

## MeltySynth

Upstream revision: `17825ce95e27295ca0c084dd51dcd73d9da93531`

Only the synthesizer/SoundFont source needed by MCG_Doom is vendored. `tools/ApplyMeltySynthCompatibility.ps1` removes unused helpers and replaces unsupported Span/MemoryMarshal/MathF/newer-BCL paths with array/scalar equivalents suitable for .NET Framework 4.7.2.

The compatibility scripts are part of the corresponding source for the release and are the authoritative description of the mechanical source transformations.
