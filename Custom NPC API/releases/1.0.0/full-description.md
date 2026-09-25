[h1]Custom NPC API[/h1]

Custom NPC API is a shared library for Big Ambitions mods that add named, interactive characters to the game world.

[b]This is a modding dependency.[/b] It does not add standalone gameplay. Install it when another mod lists Custom NPC API as a required item.

[h2]What modders can build with it[/h2]

[list]
[*] Spawn generated characters from compatible vanilla humanoid prefabs
[*] Use custom character prefabs from a mod-owned AssetBundle
[*] Configure position, facing, scale, gender, age and deterministic appearance
[*] Add clickable NPC interactions with a simple callback
[*] Open mod-registered Big Ambitions dialogs
[*] Show, hide, find and despawn NPCs at runtime
[*] Create vanilla phone contacts and append message history
[*] Preview prefab appearances and placement with an optional in-game developer window
[/list]

The consuming mod remains responsible for quests, schedules, movement, rewards, relationships, map markers and its own save data.

[h2]For mod authors[/h2]

Reference the [b]CustomNPCAPI[/b] assembly from your mod's Assembly Definition, then use [b]CustomNpcApi.Spawn[/b] with a [b]CustomNpcDefinition[/b] and [b]CustomNpcSpawnOptions[/b]. Keep the returned handle to control visibility, interaction and cleanup.

The included README and Modder Guide contain setup instructions, complete examples, field descriptions, localization requirements, custom visual integration, phone-contact helpers and troubleshooting guidance.

[h2]Compatibility[/h2]

Built for Big Ambitions build 3674.
