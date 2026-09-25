# MeltySynth vendored source

`tools/PrepareThirdParty.ps1` places the pinned MeltySynth source needed by the DOOM music backend in this folder.

Only the SoundFont/synthesizer portion is kept. The MIDI-file helper/frontend files are not needed because DOOM's original MUS data is decoded directly by `Scripts/Audio`.

MeltySynth is MIT licensed. See `ThirdParty/Licenses/LICENSE_MeltySynth.txt` after preparation.
