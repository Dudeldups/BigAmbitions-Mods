# DOOM runtime data

`tools/PrepareThirdParty.ps1` prepares the runtime files in this folder:

- `doom1.wad` — the unmodified DOOM 1.9 shareware IWAD
- `Audio/TimGM6mb.sf2` — General MIDI SoundFont used to synthesize the WAD's original MUS score
- `Legal/` — redistributed notices/licenses

The mod intentionally keeps these files under `Config/Doom/` because the Big Ambitions external mod build copies the complete `Config` directory into `ModsLocal`.

Release packages are intended to contain these prepared files, so players do not need to provide a WAD, SoundFont, or other audio data themselves.
