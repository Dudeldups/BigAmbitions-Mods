# DOOM shareware data notice

This mod is designed to ship the original, unmodified `doom1.wad` from DOOM 1.9 shareware so that players do not have to supply game data themselves.

The shareware IWAD remains copyright **id Software**. It is not relicensed as part of this mod and is kept as a separate file under `Config/Doom/doom1.wad`.

A useful distribution record is Debian's `doom-wad-shareware` package. Its copyright file preserves the original Limited Use Software License Agreement and a 1999 clarification from John Carmack stating that the DOOM shareware WAD is freely distributable.

Reference:

`https://sources.debian.org/src/doom-wad-shareware/1.9.fixed-5/debian/copyright/`

The preparation script downloads Debian's unchanged upstream shareware archive and verifies the archive's published MD5 before extracting the WAD.
