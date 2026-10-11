# SourceMaps: plan and history

The multi-map bhop world: what was asked, what was decided, and what each step built and measured. (File name kept as
MAPVOTE_PLAN.md so older links work.) Current state and rules: [handoff](HANDOFF_SOURCEMAPS.md); tests:
[TESTING.md](TESTING.md).

## Contents

1. [Goal](#1-goal)
2. [Decisions](#2-decisions)
3. [Design](#3-design)
4. [Risks and how they turned out](#4-risks-and-how-they-turned-out)
5. [Progress](#5-progress): [1](#step-1-bsp-reader-markers-collision) · [2](#step-2-converter-comparison) · [3](#step-3-map-rotation) · [4](#step-4-sample-world) · [5](#step-5-lightmaps-runs-final-checks) · [6](#step-6-bhop-blocks-prop-lighting) · [7](#step-7-breakable-glass) · [8](#step-8-bhop_monster_jam-sounds-skies-route-bot)
6. [Context for a new session](#6-context-for-a-new-session)

---

## 1. Goal

A CS:S bhop-server style VRChat world plus a reusable package:

| # | Wanted |
|---|---|
| 1 | Lobby with a vote screen: random maps from a pool, each with name, thumbnail, live votes |
| 2 | Players teleported into the winning map; vote to change map or go back; owner can force a return |
| 3 | Own package, independent of SourceMovement/SourceTimer but working with them |
| 4 | Map conversion from CS:S BSPs ([uSource](https://github.com/DeadZoneLuna/uSource) or [USource](https://github.com/Shane-SDK/USource), whichever is better) |
| 5 | Gameplay entities as markers keeping their data (teleports, zones, boosters...) |
| 6 | Maps from GameBanana, all tested with the movement in Unity + ClientSim |

## 2. Decisions

Answered 2026-10-09 (updates later marked).

| # | Question | Answer |
|---|---|---|
| 1 | Mode | **Vote**; the winner pulls everyone. *Choose* mode removed: one map active at a time, lobby always open (Back to lobby / Rejoin map) |
| 2 | Vote rules | 60 s from the first vote; the same 5 random maps for everyone, re-rolled each round; ties at random; no minimum. Owner can **lock** (only Start ends it) |
| 3 | In-map voting | Rock the vote at 60% **plus** a time limit (30 min, "extend" in the vote); Inspector option for RTV only |
| 4 | Owner | Full admin (force lobby, force a map, extend, start). Owner = instance creator, else the master |
| 5 | Pool | 5 maps to start (now 6 with monster_jam), PC only |
| 6 | Maps | bhop_japan, bhop_kitsune, bhop_eazy_v2, bhop_arcane_v1, bhop_badges; later bhop_monster_jam. No surf maps |
| 7 | Game content | Read from the world creator's own CS:S folder (as uSource does) |
| 8 | Public world | The user asks map authors; the vote menu credits them |
| 9 | Bhop blocks | Work as on CS:S servers, with a per-map **On** switch |
| 10 | Markers | Every entity with its keyvalues and outputs; working teleports, boosters, water, ladders |
| 11 | Timer zones | From [zones-cstrike](https://github.com/srcwr/zones-cstrike); by hand when a map has none |
| 12 | Records | Per map and per category (auto/legit) in the lobby, saved with VRChat Persistence; plus practice bhop/surf courses |
| 13 | Thumbnails | Rendered from the spawn, with an override slot |
| 14 | Converter | Hybrid: converter for visible geometry and textures, our BSP reader for collision and entities |

## 3. Design

Renamed from MapVote to **SourceMaps** (the user: "MapVote" sounds like only the voting). Folder `Assets/SourceMaps`,
menu **Tools > Source Maps**; no dependency on SourceMovement/SourceTimer (found by name when present).

| Part | Role |
|---|---|
| `Editor/Bsp/*` | Own BSP reader (VBSP 19-21): entities, brushes, displacements, lightmap/ambient lumps, static props |
| `SourceMapImporter` | Collision, markers (`SourceEntity`), teleports, pushes, boosters, water, ladders, bhop blocks, glass |
| `SourceMapVisuals` | Drives uSource; lightmaps, water, sky faces, prop lighting, sounds (`SourceMapSounds`), sky |
| `SourceMapManager` | Synced vote, timer, RTV, time limit, owner actions, travel |
| `SourceMapScreen`, `SourceMapButton` | Vote menu and map menus (UI), their buttons |
| `SourceMapInfo` | Per map: name, author, thumbnail, sky, spawn, time limit |
| `SourceMapsSetup` | Add To Rotation, Create Lobby, Build Sample World, Build Test Scene |

## 4. Risks and how they turned out

| # | Risk found at the start | Outcome |
|---|---|---|
| 1 | Neither converter imports triggers | Own BSP reader for entities and collision (step 1) |
| 2 | Bhop maps rely on clip/nodraw brushes | Collision built from the brush lump incl. clips; touching faces trimmed (step 5) |
| 3 | Timer zones aren't in the maps | zones-cstrike has them for all six maps (step 4) |
| 4 | Map mechanics (filtered teleports, pushes, boosters, bhop blocks) | All working (steps 3, 6) |
| 5 | Stock textures live in CS:S's VPKs | Read from the creator's CS:S folder; tests use a git-ignored copy (step 8) |
| 6 | Rights | Fine for private testing; public world needs author permission |
| 7 | GameBanana unreachable from the container | Resolved: downloads work with retries (`get_maps.sh`) |
| 8 | Several maps in one world | Maps 700 m apart, one active at a time |
| 9 | World size with a large pool | 5-15 maps realistic; PC only |

## 5. Progress

### Step 1: BSP reader, markers, collision

Done 2026-10-09.

| | |
|---|---|
| Built | BSP reader; collision from player-solid world brushes (incl. clips) and displacements; a marker per entity; working teleports |
| bhop_japan | 3,142 brushes + 980 displacements = 176,733 triangles, 540 markers, 56 teleports |
| Results | `Dev/Tests/Bsp` 18/18; real Unity: stands at 40/40 destinations, 55/55 reachable teleports work |
| Found | Thin floor triggers never fire for VRChat's floating capsule: triggers raised 8 units |

### Step 2: converter comparison

Done 2026-10-09 on bhop_japan in real Unity (`Dev/Tests/Converters/compare.sh`). Alignment = 2,575 vertical rays round
every spawn/destination, visual geometry vs our collision.

| | DeadZoneLuna/uSource | Shane-SDK/USource |
|---|---|---|
| Fixes to run in a VRChat project | 2 | 6 |
| Render pipeline | Built-in (what VRChat needs) | URP shader graphs (pink) |
| Import time | 136 s | 177-353 s |
| Output | 476 renderers, 649k triangles, 144 materials | 1,664 renderers, 159k triangles, 0 materials |
| Alignment with our collision | **79% within 5 cm**, median 0 | 5%, median 1.4 m |
| Licence | none stated | none stated |

**Choice: uSource** for visuals, installed by the world creator (not distributed).

### Step 3: map rotation

Done 2026-10-10.

| | |
|---|---|
| Built | `SourceMapManager`, `SourceMapScreen`, `SourceMapButton`, `SourceMapInfo`; Add To Rotation, Create Lobby, Build Test Scene; practice courses with saved boards (SourceTimer) |
| Owner changes | All vote state synced; leavers' votes dropped; admin = instance owner, else master |
| Importer | `trigger_push`, basevelocity/gravity boosters, `trigger_gravity`, water (layer 4), ladders (layer 22) |
| Results | `run.sh vote` 44/44; movement suite all passed; bhop_japan 40/40 + 55/55 |

### Step 4: sample world

2026-10-10.

| | |
|---|---|
| Built | Static props, `SourceMapVisuals`, `SourceMapZones` (zones-cstrike), course boards in the lobby, Build Sample World |
| Results | `Dev/Tests/Bsp` 56/56; `run.sh sample` 34/35 (one arcane out-of-bounds teleport, explained in step 5) |

### Step 5: lightmaps, runs, final checks

2026-10-10.

| | |
|---|---|
| Lighting | The maps' own lightmaps saved as textures on `SourceMaps/Lightmapped` (uSource's runtime list isn't saved with the scene) |
| Collision | Faces where brushes touch dropped (they stopped surfers) |
| Runs | Bhop and surf runs in `run.sh map`: 0 stalls on every map after two movement fixes |
| Arcane miss | A teleport sharing its box with an updraft push: map design, not a bug |
| Results | `run.sh sample` all passed (teleports 89/89); stand japan 40/40, eazy 46/46, badges 55/55, kitsune 20/20, arcane 185/185 |

### Step 6: bhop blocks, prop lighting

2026-10-10.

| | |
|---|---|
| Name blocks | `SourceMapNameTrigger` renames the player; a filtered teleport catches who stays (japan, arcane, badges) |
| Door blocks | `SourceMapDoor`: `func_door` sinks 9 units and returns; over a teleport it drops you in (eazy_v2) |
| Touches | Tested with Source's player box, not VRChat's capsule (timing) |
| Prop lighting | Ambient cube + 4 strongest visible lights, baked into vertex colours (`SourceMaps/Prop`) |
| Results | Blocks (stand / bounce / off) pass on japan, badges, arcane, eazy; `Dev/Tests/Bsp` 74/74 |

### Step 7: breakable glass

2026-10-10, by the movement session.

| | |
|---|---|
| Why | eazy_v2's red lanes 3 and 4 have glass doors you knife in CS:S |
| Rule | `func_breakable` with 0 < health ≤ 15 (one knife hit), not trigger-only or unbreakable; `func_breakable_surf` always; upright panes only |
| How | `SourceMapBreakable`: breaks when CS:S's box is within knife reach (48 units), locally, until you leave |
| Results | `Dev/Tests/Bsp` 87/87; 4/4 panes break at 400 and 1500 u/s and stay broken; sample 44/46 |

### Step 8: bhop_monster_jam, sounds, skies, route bot

2026-10-11 (overnight request).

| | |
|---|---|
| Map | bhop_monster_jam (Aoki, GameBanana 125558): 2,706 brushes, 131 displacements, 62 teleports, 454 door blocks, 25 sounds, sky `sky_day01_01` (HL2) |
| Stages | Carmac's 2:50 run (`Dev/docs/media/Monster jam-compressed.mp4`): c > r > ch > e > i/g/f > en > o > d > b > fl > tower > s (moon) > end |
| Sounds | `SourceMapSounds`: `ambient_generic` → AudioSource within its radius; loops with a cue chunk; triggers play them |
| Skies | `<map>_sky.mat` on `SourceMapInfo`, shown in that map |
| Route bot | Route files, floor scan under roofs, real gravity, push progress; `Dev/Tools/MapView` |
| Results | `Dev/Tests/Bsp` 98/98; monster_jam map test: teleports 50/50, blocks, bhop runs pass, one pyramid surf stalls; sample 63/65 |
| Bot | r, o (2), b (3), tower done; c to c8; e to its last jump; others fall early. Video `Dev/docs/media/bhop_monster_jam_bot.mp4` (2:38) |

**After step 8** (same day): lighting like CS:S (detail/translucent surfaces lightmapped, water shader, sky faces
hiding other stages, no Unity shadows; 702 lit surfaces on monster_jam, was 383), triggers widened to the CS:S hull,
lobby and map panels as UI menus (vote test 47/47). Open items: [handoff](HANDOFF_SOURCEMAPS.md#5-open-issues).

## 6. Context for a new session

| Topic | |
|---|---|
| Repo | `bunnnyy/vrc-claude`, branch `claude/vrchat-source-movement-mn0xl0`; read the [README](../../README.md) first |
| Unity | 2022.3.22f1 from `https://download.unity3d.com/download_unity/887be4894c44/LinuxEditorInstaller/Unity.tar.xz` into `/opt/unity`; run with `xvfb-run -a Unity -batchmode ...` |
| Licence | `Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-ulf --username ... --password ...` (env `UNITY_EMAIL`/`UNITY_PASSWORD`, never printed); `--return-ulf` at the end. The editor's own `-username/-password` don't activate Personal licences |
| vpm | `dotnet tool install --global vrchat.vpm.cli`, `vpm install templates`, `vpm new <name> World -p <dir>`, `vpm add package com.vrchat.worlds@3.10.5 -p <project>` |
| ClientSim | Needs its startup menu accepted and the Input System told to ignore focus (`PlayTestRunner.cs`) |
| Unity lessons | PhysX treats an exactly touching box as starting inside; VRChat keeps velocity through `TeleportTo`; new U# program assets need `ScriptVersion = CurrentVersion`; UdonSharp limits: [agent-skills-vrc-udon](https://github.com/niaka3dayo/agent-skills-vrc-udon) |
