# Third-party notices

## Managed Doom

Project: Managed Doom  
Upstream: `https://github.com/sinshu/managed-doom`  
Pinned revision: `9365696eb44326a3aab72c4bab217f7db8a87c96`  
License: GNU General Public License, version 2 or later (GPLv2-or-later)

MCG_Doom vendors the platform-independent source from `ManagedDoom/src` and removes the Silk.NET desktop frontend. The Unity/MoreComputerGames adapter is compiled together with the modified source. The complete GPL v2 text and the upstream Managed Doom license are included under `Config/Doom/Legal/`.

The .NET Framework 4.7.2 compatibility scripts modify the vendored source only as required for the Big Ambitions SDK build target. The exact upstream revision and preparation hashes are recorded by `THIRD_PARTY_PREPARED.txt`; the source modifications are summarized in `ThirdParty/MODIFICATIONS.md` and `Config/Doom/Legal/THIRD_PARTY_MODIFICATIONS.md`.

## MeltySynth

Project: MeltySynth  
Upstream: `https://github.com/sinshu/meltysynth`  
Pinned revision: `17825ce95e27295ca0c084dd51dcd73d9da93531`  
License: MIT

MCG_Doom vendors the SoundFont synthesizer source required for music playback. MIDI-file/frontend helpers that are not used by the mod are omitted. The exact upstream MIT license is included as `Config/Doom/Legal/LICENSE_MeltySynth.txt`.

## TimGM6mb SoundFont

File: `Config/Doom/Audio/TimGM6mb.sf2`  
Source package: Debian `timgm6mb-soundfont` 1.3  
Copyright: 2004 Tim Brechbill; 2010 David Bolton  
License: GPL-2

The SoundFont supplies the General MIDI instruments used to synthesize the original DOOM MUS score. The preparation script downloads Debian's source archive, verifies its SHA256, and extracts the unmodified SoundFont. Debian's complete package copyright information is shipped as `Config/Doom/Legal/TimGM6mb_DEBIAN_COPYRIGHT.txt`; the GPL v2 text is included as `Config/Doom/Legal/GPL-2.0.txt`.

## DOOM shareware IWAD

File: `Config/Doom/doom1.wad`  
Copyright: © 1994 id Software  
Distribution: original DOOM shareware terms plus the distribution clarification preserved by Debian's `doom-wad-shareware` package.

The IWAD is kept unmodified and separate from the mod/engine code. The complete Debian copyright record, including the shareware license and John Carmack's distribution clarification, is shipped as `Config/Doom/Legal/DOOM_SHAREWARE_DEBIAN_COPYRIGHT.txt`. See also `DOOM_SHAREWARE_NOTICE.md`.

## More Computer Games

Project: BigAmbitions_LIB_BA_MoreComputerGames  
Upstream: `https://github.com/capisoft-lib/BigAmbitions_LIB_BA_MoreComputerGames`  
License: MIT

MCG is a separate runtime dependency and is not bundled into MCG_Doom.
