# McLaren Senna release readiness — 12 October 2026

## Result

The current implementation passes the technical checks below. The user confirmed
NPC wheel placement and varied paint in game, then confirmed all vehicle functions
work on 12 October 2026. Development is accepted. First Workshop preparation is
documented below; the mod has not been published.

No runtime tuning or vehicle assets were changed during this review.

## Evidence and accepted behavior

| Area | Evidence / status |
| --- | --- |
| Driving, turning circle, acceleration and stronger brakes | User accepted the final driving behavior. Latest qualified report: `tools~/drive-analysis-20261012-132834.json`; no further tuning required. |
| Dealer purchase paint | User confirmed ordered paint appears before first entry. |
| Wash bay | User confirmed wash completion and driving away. Native fixture also executes two complete washes and validates cleanup. |
| NPC traffic | Presence, wheel placement and varied colors are user-confirmed. |
| Player geometry and occupant | Fresh Unity fixture checks four circular centered wheels, contact/caliper placement, driver occupancy and steering anchors. |
| Appearance | Parked ordered colors, repeat setup, repaint and per-instance material isolation pass. |
| Deformation and repair | Fixture covers front/rear lenses, low front splitter, front/rear supports and spoiler; repeated dents stay bounded and repair restores owned meshes/bindings. |
| Dynamics and audio | Factory curve and transmission safeguards, native brake modifiers, bounded upper-RPM audio pitch and three zero-RPM engine recoveries pass. |
| Dealer and traffic integration | Native prefab/lookup and traffic allocation fixtures pass, including target ownership and no late live-pool append. |
| Windows/macOS bundles | Stored prefab and loaded Windows bundle checks pass; both manifests include five native NWH scripts, StateDefinition, SurfaceMap and valid CRCs. macOS has not been observed in game on this Windows host. |
| Spender isolation | Unity dependency and component/name checks find no references to AudiRS6R, BMW_M4_G82 or Porsche_911_GT3_RS in the target vehicle graph. |
| Diagnostics | Global and feature defaults are false; opt-in auto-parking marker absent from source and installed package. |
| Localization and Unity assets | English vehicle-name locale and all four asset JSON files parse; Unity assets have matching metadata, no orphan metadata or duplicate target GUIDs. Unity ignores `.gitignore` and `tools~`. |
| Attribution | Model author Ddiaz Design, source, declared CC BY 4.0 license/link, model hash and adaptation notice are present; attribution is included in Config. Audio is synthesized. |
| Preview | Existing thumbnail inspected: 768 × 768 PNG, 668951 bytes, below the uploader's 1 MiB limit. |
| Installed package | DLL, Config, Locales and both bundles present. Basic package scan finds no forbidden source/debug/archive files or embedded user-profile traces. This is not a successful uploader preflight. |

Independent fresh-process Unity verification:
`McLarenSennaGeometrySetup.VerifyStoredAndBuilt`, Unity 2022.3.62f2,
12 October 2026, exit 0. Repaired installed-game assembly matches the verified
isolated-project assembly.

The corrected runtime was built with the restored installed-game DLLs and
directly installed before the user's successful NPC test. The final review build
also has 0 warnings/errors and the identical DLL. Because the game was running,
the normal installer queued the documentation/config copy under
`obj/ExternalModBuild/PendingInstalls/McLaren_Senna/20261012-181157/` for game exit.
The deferred installer subsequently completed the copy after the game exited;
its final target is the existing ModsLocal McLaren_Senna folder. The installed DLL
matches the verified runtime. DLL SHA256:
`158B8A537A0DB8F2B3E354E520BECF38865F9DC4149082F00F0DC4C4683B8040`.

Unchanged, verified and installed bundle hashes:

- Windows: `EEC8A4D5DD957A9AFFE32EA811A682FE5D05E2EA1B1841FCB8ABA6E3216FACD1`.
- macOS: `D20AF2D4D655AD9915DF0A237678CA289877E75866613DFF2D8C010A041FEAAB`.

The inspected current Player.log contains native pedestrian-spawner/culling
exceptions and warnings from other mods. No target-specific McLaren failure was
found in that log; this does not establish the cause of unrelated game errors.

## First Workshop preparation

The user confirmed all vehicle functions work in game on 12 October 2026.
BAMODS-67 and the development parent BAMODS-38 are accepted, alongside the
previous NPC/dealer/wash/driving confirmations.

- User-requested GPT-6 Luna / high sub-agents prepared the English description
  and the same 20 additional languages used by Porsche, with matching Unity
  metadata. The initial changelog remains English in `releases/0.1.0/`.
- The description follows the Porsche/Lamborghini vehicle structure and includes
  the exact user introduction, McLaren speed/acceleration/storage/dealers/price,
  the standard Porsche acceleration/braking sentence and the model attribution.
  The Workshop descriptions do not mention a braking percentage.
- The private `McLarenSenna.json` uploader manifest uses technical ID
  `McLarenSenna`, semantic source version `0.1.0`, initial metadata modVersion 0,
  and targetBuildNumber 3682 from the installed game's "Loaded Big Ambitions"
  log. Its paths use `%BIG_AMBITIONS_REPO%` and target `Output/McLaren_Senna`.
- No prior McLaren manifest or remembered item was found. The prepared first
  creation uses publishedFileId 0 and defaults to Private; no Public visibility
  was requested.
- Parent review corrected the dependency declaration: Modded Vehicles Integration
  (3796393608) provides the modified dealer desks/UI path. Absence of a compiled
  cross-mod type reference does not prove runtime independence. Its ID is
  verified against the local uploader state and installed Workshop item.
  This requires a Workshop Required Item, not an extra DLL reference.
- `tools/release/public-mods.json` now registers `McLaren_Senna` with
  `publicSource: true`. This is the only required shared release-configuration
  change; other vehicle mods were not edited.
- The accepted PR is integrated through the private repository workflow after
  synchronizing with current main and rebuilding/installing. Final Workshop
  package/preflight uses clean current main and is recorded in BAMODS-71 and PR25.
- Publication remains a separate user action. The preparation request does not
  authorize Steam mutation. A later authorized publication must use
  `tools/release/PublishMod.ps1` from clean current main for the final build,
  checks, one Steam attempt and public-source sync.

## Skill maintenance

The local `big-ambitions-private-driver` appearance/lifecycle reference now
documents the user-confirmed direct Gley wheelGraphics hub ownership, geometry
calibration, pre-initialization binding, single pose writer, separate calipers and
pooled/native-job validation. Its trigger now explicitly includes native traffic
wheel-animation repair. The skill's frontmatter and reference
links were checked; the stock quick validator could not start because its Python
environment lacks PyYAML. No dependency was added.

Tracked release review: BAMODS-71. Confirmed NPC fixes: BAMODS-68 and BAMODS-69.
