# Multi-map bhop/surf world: plan, verification and questionnaire

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
8. **Everything tested with the existing movement system** (Tests/UnityPlay with ClientSim).

## Verification: what holds up, what doesn't

### Fine as planned
- Creator Companion: on Linux only the CLI exists (`vpm`, `dotnet tool install --global vrchat.vpm.cli`).
  `Tests/UnityPlay/run.sh` already creates projects from VRChat's official World template with it.
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
- Tests: `dotnet run --project Tests/Sim`, `UDON_TEST=1 Tests/UdonCompile/compile.sh` (with
  `Tests/UdonCompile/setup.sh`), `Tests/UnityPlay/run.sh` (real Unity + ClientSim at 30/90/144 fps,
  47 checks).
- Unity: download 2022.3.22f1 from
  `https://download.unity3d.com/download_unity/887be4894c44/LinuxEditorInstaller/Unity.tar.xz`, extract to
  `/opt/unity`. License: env vars `UNITY_EMAIL` / `UNITY_PASSWORD` (never print them).
  `Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-ulf --username ... --password ...`
  activates the Personal license; `--return-ulf` returns it at the end. The editor's own
  `-username/-password` does not activate Personal licenses.
- Run Unity headless with `xvfb-run -a Unity -batchmode ...`. ClientSim needs its startup menu accepted
  and the Input System told to ignore focus (see `Tests/UnityPlay/PlayTestRunner.cs`).
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

1. **Mode:** *vote* is the default; the winning map pulls **everyone** in the instance. *Choose* stays
   available as an Inspector option.
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
    with VRChat Persistence.
13. **Thumbnails:** rendered in the editor from the map spawn, with a per-map override slot.
14. **Converter:** hybrid: converter for visible geometry and textures, our own BSP reader for
    collision (incl. clip brushes) and entities. Both converters compared and reported.

## Final design (proposed, waiting for go-ahead)

`Assets/MapVote/` (no dependency on SourceMovement/SourceTimer; it finds them by name if present):
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
3. MapVote runtime (manager, screen, travel, teleport) + vote logic tests in Tests/Sim + Udon compile.
4. Sample world with the 5 maps; ClientSim play tests per map with SourceMovement.
5. Persistent per-map leaderboards, thumbnails, docs.
