# Settings reference

Every changeable setting of SourceMovement, SourceTimer and SourceMaps. Building a world step by step: the
[setup guide](GUIDE.md). Back to the [README](../README.md).

> [!NOTE]
> Settings are on components in the Inspector. Defaults are what the prefabs and tools create. Distances and speeds
> are in **Source units** (1 unit = 0.01905 m) unless a row says metres, so CS:S server and map values copy over.

## Contents

| Package | Sections |
|---|---|
| **SourceMovement** | [1. SourceMovement](#1-sourcemovement) · [2. Children of SourceMovement](#2-children-of-sourcemovement) · [3. Map triggers](#3-map-triggers) |
| **SourceTimer** | [4. RunTimer](#4-runtimer) · [5. TimerZone](#5-timerzone) · [6. Leaderboard](#6-leaderboard) |
| **SourceMaps** | [7. SourceMapManager](#7-sourcemapmanager) · [8. SourceMapInfo](#8-sourcemapinfo) · [9. Map mechanics](#9-map-mechanics) |
| **Project** | [10. VRChat and Unity settings](#10-vrchat-and-unity-settings) |

---

## 1. SourceMovement

`SourceMovement.prefab`, root object.

### 1.1 Movement cvars

| Setting | Default | CS:S cvar | What it does |
|---|---|---|---|
| Gravity | 800 | `sv_gravity` | Higher falls faster and jumps lower |
| Accelerate | 5 | `sv_accelerate` | How fast you reach running speed on the ground |
| Air Accelerate | 1000 | `sv_airaccelerate` | Air strafe gain. CS:S default 10, surf servers 100-150, bhop servers 1000 |
| Friction | 4 | `sv_friction` | Ground friction (also water friction) |
| Stop Speed | 75 | `sv_stopspeed` | Below this, friction acts as if you were this fast, so you stop quickly |
| Max Speed | 250 | | Running speed (CS:S knife/pistols 250, rifles ~215-230). Swimming is 80% of it |
| Max Velocity | 3500 | `sv_maxvelocity` | Cap per axis (falling speed limit) |
| Air Speed Cap | 30 | | Air wish speed cap; it's what makes air strafing gain speed. Leave at 30 |
| Jump Impulse | 301.99 | | Jump height = Jump Impulse² / (2 × Gravity) = 57 units. **Change with Gravity** |
| Step Size | 18 | `sv_stepsize` | Highest step walked up without jumping |
| Tick Rate | 100 | `-tickrate` | Ticks per second (bhop servers 100, CS:S default 66) |

### 1.2 Player hull

| Setting | Default | What it does |
|---|---|---|
| Hull Width | 32 | Width and depth of the collision box |
| Hull Height | 62 | Standing height (CS:S 62; HL2 and GMod 72) |
| Duck Hull Height | 45 | Ducked height (CS:S 45; HL2 36). Ducked you fit 46+ unit gaps; crouch jumps reach 65-unit ledges |

### 1.3 World

| Setting | Default | What it does |
|---|---|---|
| Meters Per Unit | 0.01905 | Size of one Source unit. **Must match the scale the maps were imported with** |
| Collision Layers | Default, Environment, Walkthrough | Layers the player collides with |
| Hull Only | prefab: empty; Create Lobby adds every map | Maps whose solid colliders only the Source hull hits: while Source movement is on they move to VRChat's **Walkthrough** layer (17), so VRChat's capsule (~84 units, doesn't shrink when crouching) can't snag in vents. Objects with a trigger collider keep their layer |
| Ladder Layers | 22 | Ladder volumes (trigger boxes against the climbable face) |
| Water Layers | 4 (Water) | Water volumes (trigger boxes filling the water) |

### 1.4 Controls

| Setting | Default | What it does |
|---|---|---|
| Active On Start | on | Off: Source movement only inside the MovementZone ([section 2](#2-children-of-sourcemovement)) |
| Auto Bhop | on | Hold jump to keep hopping. Off: a new press per hop (the scroll wheel works). Runs with it on count as "auto" |
| Auto Bhop Toggle Key | B | Desktop key that switches auto bhop |
| Duck Key | Left Ctrl | Desktop key that ducks while held |
| Duck Head Fraction | 0.75 | Also duck when the head is below this share of eye height (VRChat's crouch, or crouching in VR). 0 = key only |

### 1.5 Fixed in code (same as CS:S)

| Value | | Value | |
|---|---|---|---|
| Forward/side speed | 450 | Walkable slope | 0.7 (steeper than ~45.6° is a surf ramp) |
| Ladder climb / jump-off | 200 / 270 u/s | Swim up / sink | 100 / 48 u/s |
| Water jump | 256 u/s | Eye height | 64 (ducked 47) |
| Duck / stand up on the ground | 0.4 / 0.2 s (instant in the air) | Ducked ground speed | 34% |

### 1.6 Presets

| Preset | Air Accelerate | Tick Rate | Auto Bhop | Notes |
|---|---|---|---|---|
| Bhop server (default) | 1000 | 100 | on | |
| Surf server | 150 | 100 | on | 100-150 is typical |
| Stock CS:S | 10 | 66 | off | Max Speed 250 (knife) |

## 2. Children of SourceMovement

| Component (object) | Setting | Default | What it does |
|---|---|---|---|
| SourceSpeedometer (Speedometer) | Show | off (on in the test map) | Horizontal speed in front of your view |
| | Head Offset | 0, -0.3, 1 m | Where the number floats relative to your eyes |
| | Movement, Label | set by the prefab | What it reads and its text |
| SourceMovementZone (MovementZone (optional)) | Box Collider size | object disabled | Enable it and turn **Active On Start** off: Source movement only inside the box |
| AutoBhopButton (AutoBhopButton) | Movement, Label | set by the prefab | World button (Interact) toggling auto bhop, for VR. Move it anywhere |

## 3. Map triggers

The map importer adds these; you can also place them by hand.

| Component | Setting | Default | What it does |
|---|---|---|---|
| SourcePushTrigger (`trigger_push`) | Push | 0, 0, 0 | Push velocity (u/s). Inside it moves you; leaving keeps it as momentum. Upward pushes lift |
| | Movement | empty | Empty = finds the object named `SourceMovement` |
| SourceBoostTrigger (boosters, `trigger_gravity`) | Add Velocity | 0, 0, 0 | Added once (u/s); 0, 600, 0 launches you ~225 units up |
| | Set Gravity / Gravity Scale | off / 1 | Gravity multiplier (0.5 = half). Reset on respawn |
| | On Leave | off | Fire on leaving instead of entering |
| | Movement | empty | As above |

> [!TIP]
> Ladders and water need no script: a trigger Box Collider on layer 22 (ladder) or 4 (water).

---

## 4. RunTimer

`SourceTimer.prefab`, RunTimer object.

| Setting | Default | What it does |
|---|---|---|
| Leaderboard | Leaderboard (Legit) | Board for legit runs |
| Auto Leaderboard | Leaderboard (Auto) | Board for auto-bhop runs |
| Category Source | SourceMovement (test map / lobby tool) | Script whose bool marks a run as the other category |
| Category Variable | autoBhop | That bool: on at any moment of a run = an auto-bhop run |
| Start Point | set by the tools | Where Restart (G) sends you |
| Label | set by the prefab | The timer text |
| Head Offset | 0, 0.25, 1 m | Where the timer floats relative to your eyes |
| Restart Key | G | Desktop key that restarts the run |

## 5. TimerZone

A trigger Box Collider plus this.

| Setting | Default | What it does |
|---|---|---|
| Zone Type | Start | **Start**: the timer starts when you leave it · **End**: finishes the run · **Checkpoint**: where Reset sends you · **Reset**: back to the last checkpoint (kill floors) · **Restart**: back to the start |
| Timer | the RunTimer | Which timer it drives |
| Respawn Point | this object | Checkpoint: where Reset sends you. Start: where Restart sends you |
| Leaderboard / Auto Leaderboard | empty | Start zones only: this course's own boards (empty = the RunTimer's) |

## 6. Leaderboard

Top 10, one time per player.

| Setting | Default | What it does |
|---|---|---|
| Title | "Best times" | Heading |
| Board | set by the prefab | The board's text |
| Save Key | empty | Saves each player's best with VRChat Persistence under this key (e.g. `bhop_japan_legit`); one key per board. Empty = this instance only |

---

## 7. SourceMapManager

Under "SourceMaps Lobby". The vote settings (Choices, Vote Seconds, Rtv Ratio, Time Limit Minutes, Extend Minutes)
are in [guide step 8](GUIDE.md#8-create-the-lobby). The rest is wiring Create Lobby fills in:

| Setting | What it is |
|---|---|
| Maps | Every map in the rotation |
| Lobby Spawn | Where players wait and vote; keep the VRC World spawn here |
| Screens | The vote menu and the map menus it updates |

## 8. SourceMapInfo

On each map's object.

| Setting | Default | What it does |
|---|---|---|
| Map Name | the BSP name | Name on the vote menu |
| Author | empty | Credit on the vote menu |
| Thumbnail | rendered from the spawn | Picture on the vote menu; drag your own over it |
| Skybox | the map's sky | Shown while you're in this map |
| Spawn | the map's first player start | Where players arrive |
| Time Limit Minutes | 0 | This map's own limit; 0 = the manager's |

## 9. Map mechanics

Made by the importer; change them only if you know the map.

| Component | Setting | What it does |
|---|---|---|
| **SourceMapBlocks** ("Bhop Blocks") | On | Ticked: bhop blocks as on CS:S servers. Unticked: every block is a plain platform |
| | Filtered, Gated (+ names) | The teleports, pushes and boosters that check the player's name |
| **SourceMapTeleport** (`trigger_teleport`) | Destination | Where the player is sent |
| | Filter Name / Negate / Class | Only for a player with that name (or class), from `filter_activator_name` / `_class`; empty = everyone |
| | Blocks | The map's Bhop Blocks |
| **SourceMapNameTrigger** (renaming `trigger_multiple`) | Names / Delays / Leave Names | The names given on touch (and on leaving) |
| | Wait | Seconds before it fires again while touched (Source's `wait`; negative = once) |
| **SourceMapDoor** (sinking `func_door`, "Touch" child) | Move Local, Speed, Wait | How far (m), how fast (m/s), how long it stays open (s) |
| | Drop Through | Lets you fall into the teleport below when fully open |
| | Visuals | The model moved with it |
| **SourceMapBreakable** (glass, "Break" child) | Reach | Knife reach in units (48) plus how far you get before the next check |
| | Solid, Visuals | Collider and model switched off when broken (for you only) |
| | Broken, Unit Scale | Stays on until you leave; metres per unit |
| **SourceMapSoundTrigger** (sound triggers, "Sound" child) | Play, Stop | Audio Sources played or stopped when you walk in |
| **SourceEntity** (markers) | all | Read-only copy of the map entity (class, name, keyvalues, outputs, volume); changing it does nothing |

---

## 10. VRChat and Unity settings

| Where | Setting | Note |
|---|---|---|
| VRC World (VRC Scene Descriptor) | Respawn Height Y | Falling below it respawns you; keep it below the lowest map (Create Lobby does) |
| Project Settings > Player | Active Input Handling | **Both** (the template's default): the B/G keys and scroll jump use the old input system |
| Project Settings > Player | Scripting Define Symbols | Must include `UDON` (the SDK adds it): without it the menus' buttons do nothing |
| Project Settings > Tags and Layers | Layers 4, 11, 17, 22 | Water, Environment (solid), Walkthrough (Hull Only), Ladder |
