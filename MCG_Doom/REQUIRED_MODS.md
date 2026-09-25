# Required mods

MCG_Doom requires the following separate runtime mods:

1. **LIB BA More Computer Games (MCG)** — technical assembly `LIB_BaComputerGames.dll`.
2. **LIB BA Unified UI** — required by MCG itself.

Do not bundle either dependency DLL inside MCG_Doom. For local development, `tools/BuildAndInstall.ps1` temporarily exposes the installed MCG DLL to the SDK external compiler and removes that temporary compile-only reference immediately after the build.

The DOOM engine source and the redistributable DOOM shareware IWAD are part of MCG_Doom itself after `tools/PrepareThirdParty.ps1` has been run. End users do not need to supply a WAD.

## Workshop publication

Set **LIB BA More Computer Games (MCG)** as the required Workshop item for the release. Do not embed `LIB_BaComputerGames.dll` in the published MCG_Doom package. MCG manages its own library dependencies.
