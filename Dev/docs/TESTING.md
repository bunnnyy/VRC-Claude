# Testing

How this project is tested, from fast simulations to real Unity with VRChat's ClientSim. Expected numbers come from
Source's own maths. Back to the [README](../../README.md).

## Contents

1. [Test suites](#1-test-suites)
2. [Movement: simulation and Udon VM](#2-movement-simulation-and-udon-vm)
3. [Real Unity play tests](#3-real-unity-play-tests)
4. [Source maps](#4-source-maps)
5. [Route bot and videos](#5-route-bot-and-videos)
6. [Tools](#6-tools)
7. [What isn't covered](#7-what-isnt-covered)

---

## 1. Test suites

| # | Command | Runs in | Checks | Last result |
|---|---|---|---|---|
| 1 | `dotnet run --project Dev/Tests/Sim` | .NET, Unity stand-ins | Movement + timer simulation (course boards, saved bests) | 80/80 (66 movement, 14 timer) |
| 2 | `Dev/Tests/UdonCompile/compile.sh` | Real UdonSharp compiler | Every script in `Assets/` compiles to Udon; editor scripts compile | 20/20 scripts OK |
| 3 | `UDON_TEST=1 Dev/Tests/UdonCompile/compile.sh` | VRChat's Udon VM | The compiled SourceMovement program against the movement tests | 66/66 (the same movement tests as suite 1) |
| 4 | `Dev/Tests/UnityPlay/run.sh 90` | Unity + ClientSim | Movement suite at one frame rate (no argument: 30, 90, 144, 300 fps) | All passed at 30, 90, 144, 300 and 600 fps |
| 5 | `dotnet run --project Dev/Tests/Bsp` | .NET | BSP reader and collision on the 6 real maps (closed brushes, no spike vertices), boosters, breakable glass | 108/108 |
| 6 | `Dev/Tests/UnityPlay/run.sh map <bsp>` | Unity + ClientSim | One map: glass, stand at every destination, teleports, bhop blocks, bhop/surf runs | eazy, badges, kitsune, monster_jam all passed; arcane 22/25 and japan 53/54 teleports (drop-probe spots) |
| 7 | `Dev/Tests/UnityPlay/run.sh vote` | Unity + ClientSim | Map rotation: voting through the UI menus, owner changes, practice courses, menu and scoreboard prefabs dragged in unwired | 52/52 |
| 8 | `SM_CSS=... Dev/Tests/UnityPlay/run.sh sample` | Unity + ClientSim | Whole sample world (6 maps with uSource visuals, skies, sounds, zones) | 62/64 (the same 2 drop-probe misses) |

> [!NOTE]
> Suites 4 and 6-8 need Unity 2022.3.22f1 (`UNITY=<path>` or `/opt/unity`), an activated licence and `vpm`.
> Set-up for a fresh container is in the [SourceMaps handoff](HANDOFF_SOURCEMAPS.md#2-setting-up-a-fresh-container).

## 2. Movement: simulation and Udon VM

| Suite | How |
|---|---|
| `Dev/Tests/Sim` | The C# scripts run against small Unity/VRChat stand-ins |
| `Dev/Tests/UdonCompile` | Downloads the Worlds SDK 3.10.5, Unity's managed DLLs and a few packages into a git-ignored cache (first run: minutes), builds VRChat's UdonSharp compiler and runs its full pipeline (Roslyn, bind, emit, Udon assembly) on every script, failing on anything Udon doesn't support |
| `UDON_TEST=1` | Then runs the compiled SourceMovement program in VRChat's Udon VM. Every extern goes through VRChat's real Udon wrapper, except Unity-native ones (`Physics.BoxCast`, input, time, euler angles), which the test world answers |

## 3. Real Unity play tests

`Dev/Tests/UnityPlay/run.sh` creates a VRChat world project in `Dev/Tests/UnityPlay/.cache` with `vpm` (the command-line
Creator Companion: `dotnet tool install --global vrchat.vpm.cli`) from VRChat's World template with Worlds SDK 3.10.5,
so it has VRChat's layers and project settings.

| Step | What happens |
|---|---|
| 1 | Copies `Assets/SourceMovement`, `SourceTimer` and `SourceMaps` in (the committed files are never rewritten) |
| 2 | Adds the defines the SDK only sets in an interactive editor: `VRC_SDK_VRCSDK3`, `VRC_ENABLE_PLAYER_PERSISTENCE`, `UDON` |
| 3 | Builds the test map with the **Build Test Scene** menu code |
| 4 | Plays it with ClientSim at 30, 90, 144 and 300 fps through a virtual keyboard on ClientSim's own input path |

300 fps is above the 50 Hz physics rate, where VRChat moves the player once per physics step with the last velocity
set. The real player is checked against the simulation:

| Area | Checks |
|---|---|
| Ground | Walk speed, stopping |
| Air | Bhop jump height, air strafe gain |
| Surf | On the PhysX ramp: stays on it, keeps speed and height |
| Teleports | `TeleportTo` and `TeleportPlayer` |
| HUD | Speedometer and timer text show |
| Legit mode | Through the world button (no pogo) |
| Timer | A timed run through the real trigger zones (checkpoint, reset zone, end zone, legit board) |

The `.unitypackage` was also imported into an empty project and passes the same tests. To export it again:

```
Unity -batchmode -quit -projectPath Dev/Tests/UnityPlay/.cache/Project -executeMethod PlayTestBootstrap.ExportPackage -smPackage $PWD/SourceMovement.unitypackage
```

## 4. Source maps

| # | Command | Does |
|---|---|---|
| 1 | `Dev/Tests/Bsp/get_maps.sh` | Downloads the 6 test maps from GameBanana (git-ignored) |
| 2 | `dotnet run --project Dev/Tests/Bsp` | BSP reader and collision checks on the real maps |
| 3 | `Dev/Tests/UnityPlay/run.sh map <bsp>` | Imports one map in Unity and play-tests it (`SM_ONLY=runs` or `glass`: only that) |
| 4 | `Dev/Tests/UnityPlay/run.sh vote` | Map rotation |
| 5 | `SM_CSS=<folder> Dev/Tests/UnityPlay/run.sh sample` | Whole sample world; `SM_CSS` is a folder with `cstrike` and `hl2` (stock textures, models, sounds, skies) |
| 6 | `SM_CSS=... SM_SHOT_DESTS=c1,r1 Unity -executeMethod LightingShots.Run -bsp <bsp> -smLightmaps on` | Screenshots of an imported map at those teleport destinations (no run); `SM_SHOT_MATERIALS=<dest>` lists what's drawn there |

## 5. Route bot and videos

A bot plays a map with only a player's inputs (A/D strafes with the view, jump, C to duck, W to run up). It plans each
hop by flying it in a model of the movement (input lag, air acceleration, the map's collision, surf faces) and checks
a hop on from where it lands.

| Command | Does |
|---|---|
| `Dev/Tests/UnityPlay/run.sh route <bsp> <zones.json> [framesdir]` | bhop_eazy_v2's built-in route (its red lane 1 ridge surf included); frames + HUD log to `framesdir` |
| `ROUTE_FILE=Dev/Tests/UnityPlay/Routes/<map>.txt ROUTE_SECTION=n run.sh route ...` | Another map's route file, from stage n ([route files](HANDOFF_SOURCEMAPS.md#4-route-files)) |
| `SCALE=960:540 Dev/Tests/UnityPlay/make_video.sh framesdir run.mp4 "title"` | mp4 with speed, map timer and keys burnt in |
| `Dev/Tests/UnityPlay/stitch_video.sh out.mp4 list.txt` | Several recordings with titles and the map's sounds mixed in |

| Map | Bot result |
|---|---|
| bhop_eazy_v2 | Sections 1-4; red lanes 3 and 4 not yet (its plan sees the glass doors as walls) |
| bhop_monster_jam | r, o (2), b (3), tower done; c to c8; e to its last jump; others open ([plan, step 8](MAPVOTE_PLAN.md#step-8-bhop_monster_jam-sounds-skies-route-bot)) |

## 6. Tools

| Tool | Does |
|---|---|
| `Dev/Tools/MapView` | Top-down map images, path search over safe floors, bhop-block chains, brush/prop/model listings (usage at the top of `View.cs`) |
| `Dev/Tests/Converters/compare.sh` | uSource vs USource comparison; also downloads uSource for the sample tests |

## 7. What isn't covered

> [!WARNING]
> ClientSim's player is not the VRChat client's, and ClientSim is one client: two real players at once and the
> final feel can only be checked in VRChat itself.
