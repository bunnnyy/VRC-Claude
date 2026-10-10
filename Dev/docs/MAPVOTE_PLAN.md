# SourceMaps (multi-map bhop world): plan, verification and questionnaire

(File name kept as MAPVOTE_PLAN.md so earlier links still work.)

Handoff from the session that built and Unity-tested the Source movement. Nothing here is built yet.
Answer the questionnaire (bottom) before building.

## What the user wants (summary)

A CS:S bhop-server style VRChat world, made with the VRChat Creator Companion, plus a reusable prefab:

1. **Lobby.** Players join in a lobby. A screen shows 4-6 random maps from a large pool, each with
   name, thumbnail and live vote count.
2. **Two modes, picked in the Inspector:** *vote* (everyone votes, winner is played by all) or *choose*
   (each player picks the map they want to play).
3. **Travel.** Players are teleported into the map. In a map they can vote to change map or go back
   to the lobby; the instance owner can force a return to the lobby.
4. **Own prefab system with a sample world.** Independent of SourceMovement / SourceTimer, but works
   with them (e.g. teleports through `SourceMovement.TeleportPlayer`, timer zones per map).
5. **Map conversion** from CS:S BSPs with one of two converters, whichever works best:
   [DeadZoneLuna/uSource](https://github.com/DeadZoneLuna/uSource) or [Shane-SDK/USource](https://github.com/Shane-SDK/USource).
6. **Auto-conversion of gameplay entities** into empty marker objects: fall/respawn teleports, timer
   start/end/checkpoints, speed boosters, jump boosters, etc. Markers keep the original data so the
   movement script can implement them later.
7. **Maps from GameBanana:** bhop_japan, bhop_kitsune, plus 3 other popular CS:S bhop maps.
8. **Everything tested with the existing movement system** (Dev/Tests/UnityPlay with ClientSim).

## Verification: what holds up, what doesn't

### Fine as planned
- Creator Companion: on Linux only the CLI exists (`vpm`, `dotnet tool install --global vrchat.vpm.cli`).
  `Dev/Tests/UnityPlay/run.sh` already creates projects from VRChat's official World template with it.
  On Windows the user uses the normal VCC app; same template.
- Scale: the movement already works in Source units (1 u = 0.01905 m), so converted maps at that scale
  play with CS:S distances and jump heights.
- Separate prefab: the map system only needs to teleport players and toggle map objects. It can call
  `SourceMovement.TeleportPlayer` when present, or plain `TeleportTo` + `SetVelocity(0)`.

### Problems / risks found
1. **Neither converter imports triggers.** Shane-SDK/USource: brush entities are "in the future",
   displacements are buggy, "not production ready". DeadZoneLuna/uSource: lists BSP entities and
   props but not brush entities/keyvalues (1.1 beta). Neither README states a license.
   **Suggestion:** use a converter only for visible geometry and textures, and write our own small
   editor-side BSP reader for the entity lump plus brush model bounds. That gives the markers
   (point 6) and is independent of the converter. Compare the converters on geometry, textures,
   displacements and collision quality.
2. **Collision.** Bhop maps rely on invisible `playerclip`/`clip` brushes and on `nodraw` faces.
   Converters often drop tool brushes. Collision must include clip brushes, and surf ramps need clean
   mesh colliders (the movement's BoxCast sweeps work against mesh colliders, but this needs testing).
   The BSP reader could build collision from the brush lump (contents SOLID | PLAYERCLIP) directly.
3. **Timer zones mostly aren't in the maps.** On CS:S bhop servers, start/end zones usually come from the
   timer plugin's database (shavit/bhoptimer, Influx), not the BSP. Some maps have named triggers
   (e.g. `climb_startzone` / `climb_endzone`) that timers pick up. Plan for: auto-detect when present,
   plus an editor tool to place zones by hand. Possibly import public zone dumps (to verify).
4. **Map-specific mechanics** that markers must capture: `trigger_teleport` + `info_teleport_destination`
   (fall respawns, often filtered), `trigger_push` (boosters), `trigger_multiple` with outputs like
   `AddOutput basevelocity ...` or `gravity` (jump/speed boosters), `trigger_gravity`, and
   `func_door`/`func_button` "bhop blocks" (platforms that drop or teleport you if you stand too
   long). Servers often neutralise bhop blocks; decide behaviour per map.
5. **Textures are not all in the BSP.** Custom textures are usually packed into the BSP, but stock
   CS:S textures and models come from the game's VPKs. Either the user supplies them from their CS:S
   install, or try the CS:S dedicated server through SteamCMD (anonymous login, app 232330), which may
   include the needed VPKs (to verify).
6. **Rights.** The maps belong to their authors, stock textures to Valve. Fine for private testing;
   for a public VRChat world, get author permission and consider replacing Valve textures.
7. **GameBanana was unreachable from the cloud container** (HTTP 503 / Cloudflare 522 on site and
   API on 2026-10-09). Found so far: *Bunny-Hop Kitsune* (CS:S) at gamebanana.com/mods/126424.
   bhop_japan (by Tony Montana) exists in many places (Steam Workshop ports, 17buddies), but no
   GameBanana page was confirmed. Retry later, use archive.org's GameBanana mirror, or have the user
   upload the BSPs.
   **Update 2026-10-09 (resolved for bhop_japan, checked by the previous session):** bhop_japan is
   gamebanana.com/mods/125304 (CS:S, by tmontana, 5,552 downloads), file `bhop_japan.zip` (75.7 MB,
   gamebanana.com/dl/283793) with one `bhop_japan.bsp` (159 MB, VBSP v20). GameBanana's API answers
   with a browser User-Agent (`/apiv11/Mod/<id>/ProfilePage`); from this container it returned 403
   once, so downloads may need retries or the user's help. Entity lump: 90 `trigger_teleport`,
   37 `info_teleport_destination`, 39 `trigger_multiple`, 2 `trigger_push`, 4 `func_button`,
   3 `filter_activator_name`, 96 `prop_physics_override`, 79 `prop_dynamic`, 48 `move_rope`,
   66 `info_player_counterterrorist`, `point_clientcommand`, `env_entity_maker`/`point_template`.
   **No named triggers** (no `climb_startzone` etc.), so timer zones need manual placement (risk 3).
   No basevelocity/gravity outputs. Pakfile lump is 108 MB (412 materials, 201 models), so most custom
   content is packed in the BSP (helps with risk 5).
8. **Several maps in one world.** In *choose* mode players can be in different maps at once, so maps
   must sit at different offsets (Source maps are up to ±16384 u ≈ ±312 m) and each client enables only
   its own map's renderers. Watch world size and memory: PC only is realistic, Quest (100 MB limit)
   is not.
9. **Large pool vs. world size.** "A large pool" in one upload costs download size and build time.
   Every map lives in the world; nothing streams in. Realistically 5-15 maps per world.

### Popular CS:S bhop map candidates (pick 3 in the questionnaire)
Classics often found on CS:S bhop servers: bhop_eazy_v2, bhop_arcane_v1, bhop_badges, bhop_monster_jam,
bhop_exodus, bhop_cobblestone, bhop_lego2. (Popularity not verified on GameBanana yet, because it was down.)

## Proposed architecture (to confirm)
- `Assets/MapVote/` (separate from SourceMovement/SourceTimer), sample scene `MapVoteSample.unity`.
- `MapInfo` (per map): name, thumbnail, spawn point, map root object, optional author/tier.
- `MapVoteManager` (synced, owner = instance master): picks N random maps, holds votes in synced arrays,
  timer, starts the map, handles rock-the-vote and owner force-lobby.
- `MapVoteScreen`: world UI with 4-6 buttons (thumbnail, name, votes), VR friendly.
- `MapTravel`: local teleport + enabling the right map root; uses SourceMovement when present.
- Editor: `Tools > Map Vote > Import BSP Entities` creates markers (`SourceEntityMarker` with
  classname, targetname, keyvalues, outputs and bounds), and optionally creates TimerZones and
  teleport triggers from them.
- Tests: ClientSim play tests per map (spawn, walk the start, a fall respawn, a booster marker exists),
  plus vote logic tests in the simulation harness.

## Questionnaire

1. **Mode default:** should *vote* or *choose* be the default? In *vote* mode, does everyone get pulled
   into the winning map, or only players standing in the lobby?
2. **Vote rules:** vote timer length? Ties broken randomly? Minimum players? Show the same 4-6 maps to
   everyone (synced), re-rolled each round?
3. **In-map voting:** CS:S style rock-the-vote (e.g. 60% of players `rtv` starts a new vote) plus a map
   time limit (e.g. 30 min)? Or only owner-forced return?
4. **Instance owner powers:** force lobby only, or also force a specific map, extend time, skip vote?
   Who counts as owner: instance creator, or the master (oldest player)?
5. **Pool size:** how many maps do you realistically want in one world? Is PC-only OK?
6. **Map list:** confirm bhop_japan and bhop_kitsune, and pick 3 more (suggested: bhop_eazy_v2,
   bhop_arcane_v1, bhop_badges). Should surf maps be in the same pool?
7. **Game content:** can you provide the CS:S `cstrike` VPKs from your install, or should we try the
   dedicated server download? Or replace stock textures?
8. **Public or private world?** If public: will you ask map authors for permission?
9. **Bhop blocks** (func_door platforms): keep their drop/teleport behaviour, make them static
   (common on servers), or decide per map?
10. **Markers:** besides teleports, timer zones, speed and jump boosters, should markers also cover
    gravity changes, ladders, water, `func_door`s, and buttons? Any to turn into working TimerZones
    straight away?
11. **Timer zones:** OK to place start/end zones by hand (editor tool) when the map has none?
12. **Leaderboards:** per map (one board per map in the lobby) and per category (auto/legit)? Should
    times persist across sessions (VRChat Persistence) or only per instance?
13. **Thumbnails:** GameBanana screenshots (rights?), or render them automatically in the editor from a
    camera at the map's start?
14. **Converter choice:** fine to use the converter only for visuals and our own BSP reader for
    collision and entities, if testing shows that works better?

## Context the new session needs
- Repo: `bunnnyy/vrc-claude`. Read `README.md` first. Previous work is on branch
  `claude/vrchat-source-movement-mn0xl0`.
- Tests: `dotnet run --project Dev/Tests/Sim`, `UDON_TEST=1 Dev/Tests/UdonCompile/compile.sh` (with
  `Dev/Tests/UdonCompile/setup.sh`), `Dev/Tests/UnityPlay/run.sh` (real Unity + ClientSim at 30/90/144/300 fps,
  47 checks).
- Unity: download 2022.3.22f1 from
  `https://download.unity3d.com/download_unity/887be4894c44/LinuxEditorInstaller/Unity.tar.xz`, extract to
  `/opt/unity`. License: env vars `UNITY_EMAIL` / `UNITY_PASSWORD` (never print them).
  `Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-ulf --username ... --password ...`
  activates the Personal license; `--return-ulf` returns it at the end. The editor's own
  `-username/-password` does not activate Personal licenses.
- Run Unity headless with `xvfb-run -a Unity -batchmode ...`. ClientSim needs its startup menu accepted
  and the Input System told to ignore focus (see `Dev/Tests/UnityPlay/PlayTestRunner.cs`).
- vpm: `dotnet tool install --global vrchat.vpm.cli`, `vpm install templates`,
  `vpm new <name> World -p <dir>`, `vpm add package com.vrchat.worlds@3.10.5 -p <project>`.
- Lessons from Unity testing: PhysX treats an exactly touching box as starting inside (BoxCast ignores it);
  VRChat keeps velocity through `TeleportTo` (call `SetVelocity(0)`); new U# program assets need
  `ScriptVersion = CurrentVersion`; UdonSharp is the constraint reference
  (https://github.com/niaka3dayo/agent-skills-vrc-udon).
- How the user works: short check-ins at each stage with numbers, simplest code that works, Source/CS:S
  behaviour as the reference, honest about what was tested in Unity vs simulated, ask before big design
  changes.

## Answers (2026-10-09)

1. **Mode:** *vote*; the winning map pulls **everyone** in the instance. **Update:** *choose* mode was
   removed: only one map is active at a time; the lobby is always reachable (Back to lobby / Rejoin map).
2. **Vote rules:** 60 s timer, starts on the first vote. Same 5 random maps for everyone (synced),
   re-rolled each round, ties broken at random, no minimum players. The master can **lock** the vote:
   the timer stops and the vote only ends when the master presses **Start**.
3. **In-map voting:** rock-the-vote (60% of players) **plus** a map time limit (default 30 min, with
   "extend" in the vote). Inspector option for **RTV only** (no time limit).
4. **Owner:** full admin (force lobby, force a map, extend, skip vote). Owner = instance creator;
   the master when the instance has no creator (public/group).
5. **Pool:** 5 maps to start, PC only. Add more after measuring world size.
6. **Maps:** bhop_japan, bhop_kitsune, bhop_eazy_v2, bhop_arcane_v1, bhop_badges. No surf maps for now.
7. **Game content:** the importer reads stock textures/models from the **world creator's own CS:S
   folder** (path set in the import window), as uSource does. In this container there is no CS:S, so
   tests run with **placeholder textures** (geometry, collision and entities are still tested).
8. **Public world:** the user will ask map authors for permission. Map screen credits each author.
9. **Bhop blocks:** imported as markers with their data, solid by default, per-map toggle for the
   original drop/teleport behaviour once the movement supports it.
10. **Markers:** everything: teleports, timer zones, boosters, gravity, ladders, water, doors, buttons
    and every other entity with its keyvalues and outputs.
11. **Fall teleports work now**, through a small teleport trigger that is **part of MapVote** (uses
    `SourceMovement.TeleportPlayer` when present, else `TeleportTo` + `SetVelocity(0)`).
    Timer zones: placed by hand with an editor tool when the map has none (bhop_japan has none).
12. **Leaderboards:** per map and per category (auto/legit), shown in the lobby, best times persisted
    with VRChat Persistence. **Update:** the lobby also gets a small practice bhop lane and surf ramp, each
    with its own saved legit/auto boards. (Persistence is per player: boards show the saved bests of
    players who have been in the instance.)
13. **Thumbnails:** rendered in the editor from the map spawn, with a per-map override slot.
14. **Converter:** hybrid: converter for visible geometry and textures, our own BSP reader for
    collision (incl. clip brushes) and entities. Both converters compared and reported.

## Final design (approved 2026-10-09)

**Renamed to SourceMaps** (user: "MapVote" sounds like only the voting; the package also has the
importer). Folder `Assets/SourceMaps/`, menu `Tools > Source Maps`. Class names below keep their roles.

`Assets/SourceMaps/` (no dependency on SourceMovement/SourceTimer; it finds them by name if present):
- `MapInfo`: name, author, thumbnail (rendered or override), spawn, map root, time limit, bhop-block toggle.
- `MapVoteManager` (synced, owned by the master): state Lobby -> Voting -> Playing; 5 random maps,
  synced vote array, 60 s timer, master lock + Start, RTV 60%, time limit + extend, owner admin
  actions, vote/choose mode. Owner = creator, else master.
- `MapVoteScreen`: lobby world UI (5 buttons: thumbnail, name, author, votes) plus master controls.
- `MapTeleport`: trigger for fall respawns / `trigger_teleport` destinations.
- `MapTravel`: local teleport + enable only the current map's root (one map at a time in vote mode).
- Editor `Tools > Map Vote`: *Import BSP Entities* (own BSP reader: entity lump + brush model bounds ->
  `SourceEntityMarker`s, working `MapTeleport`s), *Build Collision* (brush lump, SOLID|PLAYERCLIP),
  *Place Timer Zone*, *Render Thumbnails*, *Build Sample Scene*.
- Leaderboard: per map/category boards using SourceTimer's Leaderboard where present, extended with
  Persistence (PlayerData) for personal bests.

Order of work, with a check-in after each:
1. BSP reader + entity markers + collision on bhop_japan (simulation tests on the real BSP).
2. Converter comparison (uSource vs USource) on bhop_japan visuals, in real Unity.
3. MapVote runtime (manager, screen, travel, teleport) + vote logic tests in Dev/Tests/Sim + Udon compile.
4. Sample world with the 5 maps; ClientSim play tests per map with SourceMovement.
5. Persistent per-map leaderboards, thumbnails, docs.

## Progress

### Step 1: BSP reader, markers, collision (done 2026-10-09)
- `Assets/SourceMaps/Editor/Bsp/` reads VBSP 19-21 (entities, brushes, brush models, displacements);
  `SourceMapImporter` builds collision (player-solid world brushes incl. clips + displacements), one
  `SourceEntity` marker per entity, and working `SourceMapTeleport`s.
- bhop_japan: 3,142 solid brushes + 980 displacements = 176,733 collision triangles, 540 markers,
  56 working teleports, 34 filtered "bhop block" teleports kept as markers (need the movement script).
- Tests: `Dev/Tests/Bsp` 18/18 (simulated, real BSP). Real Unity 2022.3.22f1 + ClientSim + SourceMovement
  (`run.sh map`): player stands on the floor at 40/40 destinations/spawns, 55/55 reachable teleports send
  the player to their destination (1 trigger lies under the ground everywhere, so it can't be touched in
  Source either).
- Found while testing: thin floor triggers never fire for VRChat's capsule player, so teleport triggers
  are 8 units thicker on top. bhop_japan's tele_dest_33/34 hover over pillars that teleport you back to
  the same spot (you bounce until you strafe off, as in Source).
- Not done yet: static prop collision (248 static props in bhop_japan, needs the converter's models),
  displacement "no collision" flags (all 0 in bhop_japan, bit meaning not verified).

### Step 2: converter comparison on bhop_japan (done 2026-10-09)
Real Unity 2022.3.22f1 (VRChat world project), `Dev/Tests/Converters/compare.sh`. Stock CS:S content isn't
available here, so only what's packed in the BSP can show (white = stock texture, missing = stock model).
Alignment = 2,575 vertical rays around every spawn/destination, visual geometry vs SourceMaps collision.

| | DeadZoneLuna/uSource (2022-02) | Shane-SDK/USource (2025-02) |
|---|---|---|
| Gets running in a VRChat project | 2 fixes (asmdef "unsafe" flag, call setup its window does) | 6 fixes (Git LFS DLLs, unused PlasticPipe using, settings fail on first launch, URP shaders x3, no materials by default) |
| Render pipeline | Built-in (its own shaders), what VRChat needs | URP shader graphs, pink in VRChat without replacing |
| Import time | 136 s | 177-353 s |
| Output | 476 renderers, 649k triangles, 144 materials (44 stock, no texture), packed textures and models (trees) load | 1,664 renderers, 159k triangles, **0 materials** |
| Alignment with our collision | **79% within 5 cm, median 0 cm**; the rest are invisible clip/nodraw brushes and props | 5% (after turning it 90°: it maps axes as (x, z, y)), median 1.4 m |
| Saves to project | materials + PNG textures as assets, meshes inside the scene | ScriptedImporter asset (prefab) |
| Static prop collision | no (render meshes only) | yes (MDL physics), untested here |
| License | none stated | none stated |

**Choice: uSource** for the visuals (built-in pipeline, lines up with the collision, textures work).
Not distributed with SourceMaps (no license): the world creator installs uSource; SourceMaps drives it
(scale 0.01905, the creator's CS:S folder) and saves meshes as assets. Static prop collision: generate from
the render meshes (needs a test). I did not dig into why USource's geometry doesn't line up.

### Step 3: map rotation runtime (done 2026-10-10)
- `SourceMapManager` (vote, 60 s timer from the first vote, owner lock/start/force/everyone-to-lobby/+time,
  rock the vote 60%, time limit with "extend", random ties, map just played not offered again),
  `SourceMapScreen` (lobby board, panel at each map's spawn), `SourceMapButton` (Interact, desktop and VR),
  `SourceMapInfo` (per map). Editor: Add Selected Map To Rotation, Create Lobby, Build Test Scene.
- Changes asked for during the step: choose mode removed (one active map at a time, lobby always open);
  practice bhop lane and surf ramp beside the lobby with their own saved legit/auto boards (SourceTimer:
  per-course boards on start zones, Leaderboard save key with VRChat PlayerData).
- Owner changes: all vote state is synced, so a new owner carries on; leavers' votes and rtv are dropped
  (recount a frame after OnPlayerLeft / ownership transfer); admin = instance owner, else the master
  (`Networking.InstanceOwner` is null when the owner is away, per VRChat's docs).
- Importer additions for SourceMovement (by name, so SourceMaps compiles without it): trigger_push ->
  SourcePushTrigger, basevelocity/gravity boosters and trigger_gravity -> SourceBoostTrigger, water ->
  layer 4 trigger boxes, ladders -> layer 22. Filtered ones stay markers. bhop_japan: 5 water volumes, its
  2 pushes are filtered.
- Tests: real Unity + ClientSim `run.sh vote` 44/44 (voting flow, owner changes with ClientSim's simulated
  remote players, practice runs and saved bests); `run.sh 90` (movement) all passed; `run.sh map` bhop_japan
  40/40 + 55/55; `Dev/Tests/Bsp` 24/24 (incl. booster/push parsing); `Dev/Tests/Sim` all passed.
  Not testable here: two real VRChat clients at once (ClientSim is one client).

### Step 4: the sample world with all 5 maps (2026-10-10)
- Maps from GameBanana (`Dev/Tests/Bsp/get_maps.sh`, zip/rar/7z): bhop_japan, bhop_kitsune, bhop_eazy_v2,
  bhop_arcane_v1, bhop_badges. `Dev/Tests/Bsp` 56/56 on all five (arcane's 91 `*_stop` destinations sit inside
  player clips and kitsune has 7 teleports without a target: both map design, counted separately).
- Timer zones: not in the BSPs, but the zone files CS:S bhop servers use exist for all five
  ([srcwr/zones-cstrike](https://github.com/srcwr/zones-cstrike), downloaded at import, not stored here):
  start/end per track, stages as checkpoints, bonus tracks as their own courses.
- New: static props from the game lump (japan 89 solid of 248, arcane 114 of 288), `SourceMapVisuals` (runs uSource,
  removes tool surfaces Source doesn't draw, solid props get colliders, saves meshes), `SourceMapZones`,
  SourceTimer `CourseBoards` (records wall in the lobby; boards can't live in switched-off maps),
  `Build Sample World` (maps 700 m apart, thumbnails rendered from the spawn), respawn height below the deepest map.
- Real Unity + ClientSim + SourceMovement (`run.sh sample`): 34/35. Per map: forced from the board, player stands
  at the spawn, visuals without tool surfaces, sampled teleports (japan 15/15, badges 14/14, kitsune 10/10,
  eazy 7/7, arcane 3/4), start -> end run timed onto the map's saved board. The arcane miss: the player falls
  through a huge (54 x 88 x 47 m) out-of-bounds teleport without it firing; single-map arcane test: stand 185/185,
  teleports 20/27 tested (118 not reachable by the test's drop-in probe). Not fully explained yet.
- Known gaps: stock CS:S textures/models aren't available here (white surfaces, missing stock props); no lighting
  is imported (indoor maps are dark until lighting is baked; uSource can read the maps' lightmaps, untried).

### Step 5: lighting, bhop/surf runs, final checks (2026-10-10)
- **Lighting: the maps' own Source lightmaps, nothing to bake.** uSource can read them (`ParseLightmaps`) but only into
  Unity's runtime lightmap list, which isn't saved with the scene (so VRChat would never see it). `SourceMapVisuals` now
  saves each lightmap as a texture and gives each lit surface a material with `SourceMaps/Lightmapped` (texture x
  lightmap x 2, Source's overbright; unlit like CS:S world brushes). arcane: 329 surfaces lit, eazy_v2: 484. Props keep
  Unity lighting (Source lights them per vertex). Screenshot in the guide.
- **Collision: faces where brushes touch are dropped** (`BspGeometry.AddSolidBrushes`). Source collides with whole
  brushes and never meets them; in a triangle mesh their edges sit in the surface (a ramp made of brushes side by side)
  and stopped a surfer on bhop_eazy_v2. Also fewer triangles (kitsune 70.9k -> 41.9k brush triangles).
- **Bhop and surf runs** in `run.sh map` (real Unity + ClientSim + SourceMovement): from up to 12 destinations, the
  longest open floor, W for prespeed, then jump + air strafe (A/D while turning); surf on the map's largest slopes too
  steep to stand on, holding into the ramp. A stall = speed falls > 100 u/s in a frame with no vertical wall ahead.
  Two seam stalls (a seam across a ramp, a seam along a wall) were SourceMovement's; the movement session fixed both.
- **The open arcane miss, explained:** trigger_teleport 423175 (out of bounds, to `bonus_stop`) shares its box with a
  trigger_push straight up at 1250 u/s (hammerid 422061): an updraft, so a player there is pushed up and never falls
  into the teleport's bottom slab, as in Source. Map design, not a bug. Also found: the test's drop point could start
  inside a solid block (hollow collision mesh, no face to overlap); it now checks that (`InsideSolid`), tries the 3
  best spots per teleport and follows relay teleports at the destination.
- **Results (real Unity 2022.3.22f1 + ClientSim + SourceMovement):** `run.sh sample` all passed (5 maps, lightmaps,
  sampled teleports 89/89). `run.sh map` per map: stand at every destination/spawn (japan 40/40, eazy 46/46, badges
  55/55, kitsune 20/20, arcane 185/185); teleports japan 55/55, eazy 44/44, badges 134/134, kitsune 28/29, arcane 22/25;
  bhop runs 0 stalls on 4 maps (top speeds 300-530 u/s), surf runs 0 stalls on all. `Dev/Tests/Bsp` 57/57 (incl. the trimmed
  ramp seam), `Dev/Tests/Sim` all passed, `run.sh 90` all passed, UdonSharp compile OK.
- **Still open:** arcane's 3 remaining misses are huge out-of-bounds `*_stop` volumes (326264, 326616, 326929) where
  every drop spot the test finds ends up pushed out of geometry (was 7 misses before this step). kitsune: one tiny
  trigger (0.6 m) the falling player slides off; one grounded stop at a 2 u diagonal lip near `restartred`, reported to
  the movement session (step-up). Faint lines can show where two lightmapped faces meet (lightmap atlas edges).


### Step 6: bhop blocks and prop lighting (2026-10-10, asked by the user after step 5)
- **Bhop blocks, as on CS:S servers** (`SourceMapBlocks`, one "Bhop Blocks" object per map with an **On** switch):
  - Name blocks (japan, arcane, badges): a `trigger_multiple` on the block renames the player ("!activator AddOutput
    targetname activator" at 0.09 s, "default" at 0.1 s, refiring every `wait`), and a teleport filtered on that name
    (`filter_activator_name`) covers the block. `SourceMapNameTrigger` + filtered `SourceMapTeleport`; also
    `filter_activator_class` (japan's beginner mode) and name-gated pushes/boosters (badges: 58 brushes).
  - Door blocks (eazy_v2, 259): `func_door` with "touch opens" sinks 9 units at 20 u/s and returns after 0.5 s
    (`SourceMapDoor`, collider and visuals move; local per player). Over a teleport it stops being solid when fully
    open, so the player drops into it (in CS:S they sink into it).
  - Touches of name triggers and filtered teleports are tested with Source's player box (32 x 32 x 72), not with
    VRChat's capsule: the capsule floats about 6 units above the floor (2 hover + skin), which is why all triggers are
    raised 8 units, and with capsule events the timing went wrong (a player who jumped off was still "inside").
- **Prop lighting like CS:S lights models**: the ambient cube VRAD stored in the prop's leaf (LUMP_LEAF_AMBIENT_*), plus
  the 4 strongest world lights that reach the prop (ray against the collision; the sun only if the ray ends in a sky
  brush), N.L per vertex, baked into vertex colours (`SourceMaps/Prop`); alpha-tested foliage lit from both sides.
  Ambient values are c * 2^exp (not / 255 as lightmaps are). japan: 148 of 168 prop renderers (the rest are unlit).
- **Results (real Unity + ClientSim + SourceMovement, `run.sh map`)**, bhop blocks (stand 1 s / bounce with jump held /
  blocks off): japan 6/6, 5/5, 6/6; badges 6/6, 6/6, 6/6; arcane 4/4, 3/3, 4/4; eazy doors 6/6, -, 6/6. Bounce is only
  tested on blocks whose teleport reaches at most 20 units above them (arcane has 65-unit ones that catch bouncers
  in CS:S too). badges and eazy_v2 all passed; `Dev/Tests/Bsp` 74/74.
- Collision trim fix: touching faces are now matched by the BSP's exact plane (the clipped corners can be ~0.1 unit off,
  e.g. 6847.913 vs 6848, which kept some wall joins' hidden end faces). With the movement session's e069413 the arcane
  wall-slide stalls are gone; the runs ignore speed changes inside pushes/boosters (arcane's launch pads).
- Still open: arcane's 3 out-of-bounds teleports and one japan/kitsune teleport per run in the drop-in test (the probe,
  not the triggers); kitsune's 2-unit diagonal lip (movement session).
