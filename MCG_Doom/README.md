# MCG_Doom

**Release version: 1.0.0**

DOOM as a game for the **More Computer Games** framework in Big Ambitions.

The mod follows the existing MCG game structure: a small `ComputerGameMod<TGame>` entry point registers the title, while `DoomGame` owns the Unity camera and delegates the actual game to small runtime adapters. The original Managed Doom desktop frontend is not used.

## Current implementation

The current playable implementation includes:

- original DOOM shareware game data (`doom1.wad`)
- Managed Doom software renderer and game logic
- 320x200 point-filtered Unity framebuffer
- MCG-owned camera output
- fixed 35 Hz DOOM game ticks
- keyboard and mouse controls
- native DOOM menu/automap remapped away from Big Ambitions / MCG-reserved keys
- short startup controls hint for the remapped keys
- original DOOM MUS music synthesized at runtime through MeltySynth + `TimGM6mb.sf2`
- original DOOM sound effects decoded directly from the WAD and played through Unity `AudioSource` channels
- native DOOM `Quit Game` exits cleanly back to More Computer Games

The music is not shipped as converted MP3/OGG files. The original MUS lumps stay inside `doom1.wad`; `UnityDoomMusic` decodes their events and streams synthesized stereo PCM through a Unity `AudioClip`. Sound effects are read from the original `DS*` DMX-format lumps in the same WAD, converted from unsigned 8-bit samples to Unity PCM once at startup, and played through an eight-channel `UnityDoomSound` backend plus a dedicated UI channel.

## Folder layout

```text
MCG_Doom/
├─ Config/
│  └─ Doom/
│     ├─ doom1.wad                     # added by PrepareThirdParty.ps1
│     ├─ Audio/
│     │  └─ TimGM6mb.sf2               # added by PrepareThirdParty.ps1
│     └─ Legal/
├─ Locales/
├─ Scripts/
│  ├─ Audio/                            # Unity SFX + MUS/music backends
│  ├─ Compatibility/                    # net472 compatibility helpers
│  ├─ Core/                             # lifecycle/path handling
│  ├─ Input/                            # Unity -> DOOM input bridge
│  ├─ Rendering/                        # DOOM framebuffer -> Unity
│  └─ ThirdParty/
│     ├─ ManagedDoom/                   # vendored engine source
│     └─ MeltySynth/                    # vendored synthesizer source
├─ tools/
│  ├─ PrepareThirdParty.ps1
│  ├─ ApplyManagedDoomCompatibility.ps1
│  ├─ ApplyMeltySynthCompatibility.ps1
│  └─ BuildAndInstall.ps1
├─ MCG_Doom.asmdef
└─ ModManifest.asset
```

## Prepare the third-party payload

Run once from PowerShell:

```powershell
cd Assets\Mods\MCG_Doom
.\tools\PrepareThirdParty.ps1
```

The script independently prepares whichever payloads are still missing. On an existing MCG_Doom working tree that already contains Managed Doom and `doom1.wad`, running it again will keep those files and only add the new music dependencies.

It prepares:

1. Managed Doom's platform-independent source,
2. the unmodified DOOM 1.9 shareware `doom1.wad`,
3. a pinned MeltySynth source revision for SoundFont synthesis,
4. the `TimGM6mb.sf2` General MIDI SoundFont,
5. the applicable third-party license files, and
6. a `THIRD_PARTY_PREPARED.txt` record with revisions/hashes.

Both Managed Doom and MeltySynth are patched automatically for the SDK's effective .NET Framework 4.7.2 build target. The player is **not** expected to provide a WAD, SoundFont, or any other game/audio file.

To refresh all third-party payloads deliberately:

```powershell
.\tools\PrepareThirdParty.ps1 -Force
```

Do not use `-Force` merely to add music to an already prepared working tree; the normal command is sufficient.

## Controls

Big Ambitions / MCG owns several keys that original DOOM normally uses, so the conflicting DOOM actions are remapped.

| Action | Keys |
| --- | --- |
| Move | `WASD` |
| Turn | Left / Right arrow or mouse |
| Fire | Left Ctrl or left mouse button |
| Use / Open | Space or right mouse button |
| Run | Shift |
| Weapons | `1` - `7` |
| Automap | `E` |
| DOOM menu | `P` |
| Leave computer | `Tab` (MCG) |
| MCG launcher/back | `Backspace` (MCG) |
| Big Ambitions pause | `Escape` (host) |

The normal DOOM arrow-key movement is retained where it does not conflict with the modern WASD mapping.

When a DOOM session starts, a small controls hint is shown at the bottom of the game image for about 7.5 seconds. It calls out `P = DOOM MENU`, `E = AUTOMAP`, `Backspace = game selection`, and `Tab = leave PC`, then hides automatically.

A copy-ready Steam Workshop BBCode section is included in `WORKSHOP_CONTROLS.bbcode.txt`.

## Build / install

This mod is intended to live at:

```text
Assets/Mods/MCG_Doom
```

After the third-party payload has been prepared, build/install with:

```powershell
.\tools\BuildAndInstall.ps1
```

The wrapper supplies the separately installed `LIB_BaComputerGames.dll` as a **compile-only** reference to the normal SDK external builder and retains it in the ignored SDK dependency folder so Unity can resolve the `MCG_Doom.asmdef` between builds. MCG itself is not bundled into MCG_Doom.

The SDK external build copies `Config/`, so both `doom1.wad` and `Config/Doom/Audio/TimGM6mb.sf2` are installed into `ModsLocal` with the mod.

## Audio architecture

Music and sound effects use separate Unity adapters:

```text
doom1.wad / D_* MUS lump
        ↓
DoomMusReader / DoomMusDecoder
        ↓
MIDI-style events
        ↓
MeltySynth + TimGM6mb.sf2
        ↓
stereo float PCM @ 44.1 kHz
        ↓
Unity streaming AudioClip / AudioSource
```

Managed Doom chooses the correct `Bgm` and looping behavior. The adapter resolves the corresponding `D_*` lump from the WAD, so level changes and the attract/menu music do not need a separate hard-coded track table in MCG_Doom.

Only DOOM MUS data is required for the bundled shareware episode. Generic MIDI-file playback from Managed Doom's desktop frontend is intentionally not included.

The sound-effect path mirrors Managed Doom's desktop behavior without bringing its DrippyAL/OpenAL frontend into the mod:

```text
doom1.wad / DS* sound lump
        ↓
DMX sound header + unsigned 8-bit samples
        ↓
DoomSfxLibrary
        ↓
Unity mono AudioClip
        ↓
UnityDoomSound (8 world channels + 1 UI channel)
        ↓
distance attenuation + stereo pan + optional DOOM random pitch
```

Managed Doom still owns sound selection, priorities, listener position, pause/resume calls, and per-sound volume requests. `UnityDoomSound` adapts those requests to Unity rather than hard-coding weapon, monster, door, or pickup sounds.

## .NET Framework 4.7.2 compatibility

The current Big Ambitions external build compiles this mod effectively as `net472`, while current Managed Doom and MeltySynth use some newer BCL APIs.

`ApplyManagedDoomCompatibility.ps1` handles the Managed Doom compatibility changes and also invokes `ApplyMeltySynthCompatibility.ps1` when the MeltySynth source is present. The MeltySynth patch removes unused MIDI-file/frontend helpers and replaces the small set of Span/MemoryMarshal/MathF-based code paths required by newer runtimes with array/scalar equivalents suitable for the SDK build.

Both scripts are idempotent and are run from the existing build workflow. Avoid manually applying the same compatibility edits throughout the vendored third-party source.

## More Computer Games dependency

Do **not** bundle the MCG library into this mod. It is a dependency of the game mod, just like Tetrix uses the library rather than embedding it.

## Licensing

The adapter/mod source in this folder is distributed under GPLv2-or-later together with the Managed Doom source used by it. For a public binary release, publish the complete corresponding source for this exact `1.0.0` build from the same release location (for example a tagged public repository) and link it from the Workshop page. See `LICENSE` and `THIRD_PARTY_NOTICES.md`.

- Managed Doom: GPLv2-or-later
- MeltySynth: MIT
- TimGM6mb SoundFont: GPL-2
- `doom1.wad`: original DOOM shareware distribution terms; kept unmodified and separate from the code
- More Computer Games: MIT dependency, not vendored

See the files under `Config/Doom/Legal/` in a prepared/installable tree for the redistributed notices/licenses.

## Release smoke-test checklist

After rebuilding:

1. DOOM still appears and starts from the MCG launcher.
2. Title/menu and level music still play and switch without overlapping.
3. Firing the pistol/shotgun produces the original weapon sounds.
4. Doors, switches, pickups and monster sounds play normally.
5. Nearby sounds are louder than distant sounds and pan left/right as the player turns.
6. Menu/UI sounds stay centered.
7. Pausing/resuming DOOM does not lose currently playing long sound effects.
8. Returning to the MCG launcher or leaving the computer stops all music and SFX.
9. Reopening DOOM starts a fresh audio session without duplicate channels.
10. `P` still opens the DOOM menu and `E` toggles the automap.
11. `P` → `Quit Game` exits back to the MCG launcher instead of leaving a frozen DOOM screen.
12. Save a game, load it again, and verify the save survives a fresh DOOM session.
