# Managed Doom source goes here

Run `tools/PrepareThirdParty.ps1` from the `MCG_Doom` directory.

The script vendors the platform-independent Managed Doom source into this directory and deliberately excludes its Silk.NET desktop frontend. The vendored code is compiled into `MCG_Doom.dll` together with the Unity/MoreComputerGames adapter.

Do not manually edit vendored files unless you also document the change in `THIRD_PARTY_NOTICES.md`.
