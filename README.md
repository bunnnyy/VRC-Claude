# Source Movement for VRChat

CS:S-style bhop and surf for VRChat worlds: a port of Source SDK 2013's `gamemovement.cpp` in UdonSharp, a run
timer with saved records, and an importer that turns CS:S bhop maps into a map-vote world.

> [!NOTE]
> **Status: work in progress.** Everything below is play-tested in Unity 2022.3.22f1 with VRChat's ClientSim
> ([test results](Dev/docs/TESTING.md)). Not yet tested in the VRChat client itself.

## Contents

1. [What's in it](#1-whats-in-it)
2. [Quick start](#2-quick-start)
3. [Documentation](#3-documentation)
4. [Movement features](#4-movement-features)
5. [Scripting API](#5-scripting-api)
6. [How the movement works](#6-how-the-movement-works)
7. [Repository layout](#7-repository-layout)

---

## 1. What's in it

| Package | Folder | What it does |
|---|---|---|
| **SourceMovement** | `Assets/SourceMovement` | Source movement: bhop, air strafe, surf, duck, ladders, water, boosters. All cvars in the Inspector |
| **SourceTimer** | `Assets/SourceTimer` | Start/end/checkpoint zones, run timer, top-10 boards (auto and legit bhop apart), saved bests |
| **SourceMaps** | `Assets/SourceMaps` | CS:S map import (collision, visuals, lightmaps, sounds, sky, bhop blocks), lobby with map vote, rock the vote, time limit |

The three are independent; a bhop world uses all three.

## 2. Quick start

| # | Step |
|---|---|
| 1 | Make a VRChat **World** project (Creator Companion, Worlds SDK 3.10.5+, Unity 2022.3.22f1) |
| 2 | Import `SourceMovement.unitypackage` (**Assets > Import Package > Custom Package**), or copy the `Assets/` folders with their `.meta` files |
| 3 | Try it: open `Assets/SourceMovement/SourceTestMap.unity` (bhop lane, surf ramp, ladder, pools, boosters, timer) |
| 4 | In your scene: drag in `SourceMovement.prefab` (and `SourceTimer.prefab` for timing) |
| 5 | For a map-vote world with CS:S maps, follow the **[setup guide](docs/GUIDE.md)** |
| 6 | Own lobby layout: drag in the `VoteMenu`, `MapMenu` and `Scoreboard` prefabs ([guide, step 8](docs/GUIDE.md#8-create-the-lobby)) |

## 3. Documentation

| Document | For |
|---|---|
| **[Setup guide](docs/GUIDE.md)** | Building a world step by step, from an empty project to an upload |
| **[Settings reference](docs/SETTINGS.md)** | Every Inspector setting, with defaults and presets |
| [Testing](Dev/docs/TESTING.md) | How the tests run and what they check (development) |
| [SourceMaps plan and history](Dev/docs/MAPVOTE_PLAN.md) | Design decisions and results per step (development) |
| [SourceMaps handoff](Dev/docs/HANDOFF_SOURCEMAPS.md) | Notes for whoever works on SourceMaps next (development) |
| [Ducking](Dev/docs/CROUCH.md) | How ducking matches CS:S (development) |

## 4. Movement features

Defaults are bhop-server settings: `sv_airaccelerate 1000`, `sv_accelerate 5`, friction 4, max speed 250, 100 tick.

| Feature | How it works |
|---|---|
| **Auto bhop** | Hold jump to keep hopping; **B** toggles it, the scroll wheel always jumps. VR: the `AutoBhopButton` (Interact) |
| **Ducking** | **Left Ctrl** or VRChat's crouch (**C**, or crouch in VR). Hull 62 → 45 units, 34% ground speed, crouch jump reaches 65-unit ledges (duck held as you jump) |
| **Ladders** | Walk into one to grab it; W/S climb along your view, jump pushes off at 270 u/s, climb 200 u/s. A trigger on layer 22 |
| **Water** | Waist deep: swim along your view at 200 u/s, jump swims up, water jump out at a ledge. A trigger on layer 4 |
| **Boosters** | `SourcePushTrigger` (`trigger_push`) and `SourceBoostTrigger` (basevelocity, gravity) on trigger colliders |
| **Movement zone** | Turn off **Active On Start** and enable the `MovementZone (optional)` child: Source movement only inside it |
| **Speedometer** | The `Speedometer` child (tick **Show**): horizontal speed in front of your view |
| **Hull Only** | Maps whose colliders only the CS:S hull hits, so VRChat's taller capsule can't snag in vents ([settings](docs/SETTINGS.md#1-sourcemovement)) |

Desktop and VR both work: movement comes from VRChat's move/jump input, direction from head yaw.

## 5. Scripting API

On the `SourceMovement` UdonBehaviour:

| Call | Does |
|---|---|
| `TeleportPlayer(position, rotation, keepVelocity)` | Teleport and choose whether speed is kept |
| `SetMovementActive(bool)` | Turn Source movement on or off (`_ZoneEnter` / `_ZoneExit` count zone colliders) |
| `AddPush(v)`, `RemovePush(v)` | Entering / leaving a `trigger_push` (overlapping pushes add up, like Source) |
| `AddVelocity(v)`, `SetGravityScale(s)` | Boosters (basevelocity) and player gravity (1 normal, 0 also normal) |
| `ResetVelocity()`, `ResetMapState()` | Stop; a new map (no speed, pushes or gravity left over) |
| `GetSpeed()`, `GetSourceVelocity()`, `IsOnGround()` | Read the state |

Scripts without a `SourceMovement` reference (found by name) can teleport through it: set `teleportPosition`,
`teleportRotation` and `teleportKeepVelocity` with `SetProgramVariable`, then `SendCustomEvent("_Teleport")`.

> [!TIP]
> Plain `VRCPlayerApi.TeleportTo` works too: a jump over 64 units (plus a physics step's travel) resets velocity,
> a shorter one keeps it (call `ResetVelocity` to stop) and is followed only as far as the hull fits. VRChat keeps
> the old velocity briefly after `TeleportTo`, so call `SetVelocity(Vector3.zero)` right after it.

## 6. How the movement works

| | |
|---|---|
| Player | VRChat's walk/run/jump/gravity are set to 0 while active |
| Simulation | Fixed 100-tick Source ticks every frame, on an axis-aligned box hull (32 x 62 units, 45 ducked) swept with `Physics.BoxCast` |
| Output | The real player is moved with `SetVelocity` so it lands where the simulation says |
| Units | Source units throughout (1 unit = 0.01905 m), so imported maps keep CS:S distances and jump heights |

## 7. Repository layout

| Path | Needed for a world? | Contents |
|---|---|---|
| `Assets/` | **Yes** | The three packages |
| `SourceMovement.unitypackage` | Yes (or `Assets/`) | SourceMovement + SourceTimer as one package |
| `docs/` | Yes (to read) | Setup guide, settings, images |
| `Dev/` | No | Tests, tools and internal notes ([testing](Dev/docs/TESTING.md)) |
