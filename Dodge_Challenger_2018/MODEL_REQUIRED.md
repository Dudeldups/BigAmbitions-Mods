# Local source-model requirement

The original GLB is intentionally not embedded by this chat-to-GitHub workflow because the GitHub connector used here only supports practical UTF-8 source writes, not a direct binary handoff of the uploaded 11.5 MB file.

Expected source path:

`Assets/Mods/Dodge_Challenger_2018/Models/2018_dodge_challenger_srt_demon_hpe1200.glb`

Use:

`tools/build_dodge_challenger_2018_isolated.ps1 -SourceGlb "C:\path\to\2018_dodge_challenger_srt_demon_hpe1200.glb" -Install`

The script copies the exact GLB into the mod folder before Unity generation/build. If `-SourceGlb` is omitted it also checks the user's Downloads folder for the exact filename.

Expected supplied-file SHA-256:

`38C78B40626B0FBB96BB2DE6F17B0068441BE140C380088926BDE8CB9BD7038F`

The script refuses a different file unless its hash is updated deliberately in the script/source documentation.
