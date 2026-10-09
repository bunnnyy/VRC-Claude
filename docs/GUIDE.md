# Setup guide: a CS:S bhop world in VRChat

Step by step, from nothing to an uploaded VRChat world with Source movement, a timer and a lobby where
players vote between imported CS:S bhop maps. Kept up to date as features are finished.

> **Status (2026-10-09).** Done and tested: movement, timer, map import (collision, markers, teleports),
> map rotation (vote/choose, rock the vote, time limit, owner controls). Still being built: the one-click
> visual import with uSource (step 6a is manual for now), placing timer zones on maps, per-map leaderboards,
> thumbnails rendered in the editor. Those steps say "coming soon".
> Pictures come from Unity itself; editor windows can't be screenshotted on the build machine, so menus are
> described in words.

## 1. What you need

| | Why |
|---|---|
| A Windows PC | VRChat's Creator Companion and world uploads are Windows-first |
| A VRChat account that can upload worlds | Uploading needs the "New User" trust rank or higher |
| [VRChat Creator Companion (VCC)](https://vrchat.com/home/download) | Creates the Unity project with the VRChat SDK |
| Unity Hub + **Unity 2022.3.22f1** | The VCC tells you to install this exact version (it opens Unity Hub for you) |
| **Counter-Strike: Source** (Steam) | The maps use CS:S's stock textures and models; the importer reads them from your game folder |
| This repository | Download it as a zip from GitHub (green "Code" button > Download ZIP) |
| [uSource](https://github.com/DeadZoneLuna/uSource) (DeadZoneLuna) | Converts the maps' visible geometry and textures (see step 4) |

## 2. Create the world project

1. Open the Creator Companion, **Projects > Create New Project**.
2. Pick the **World** template (Udon, with UdonSharp and ClientSim), give it a name, **Create Project**.
3. **Open Project**. Unity starts (first start takes a few minutes).

## 3. Add SourceMovement, SourceTimer and SourceMaps

1. Unzip this repository.
2. Copy the folders `Assets/SourceMovement`, `Assets/SourceTimer` and `Assets/SourceMaps` **and the `.meta`
   files next to them** into your project's `Assets` folder (drag them into Unity's Project window, or copy them
   in Explorer while Unity is closed).
3. Wait for Unity to compile. If it shows UdonSharp errors, use **VRChat SDK > Udon Sharp > Compile All UdonSharp
   Programs** once.

The three parts are independent: SourceMaps works without the other two, but you want all three for a bhop world.

## 4. Install uSource (the map converter)

uSource turns a map's geometry and textures into Unity meshes and materials. We compared it with Shane-SDK's
USource; uSource works with VRChat's render pipeline and lines up with our collision (79% of test rays within
5 cm; the rest are invisible clip brushes and props). Details in [MAPVOTE_PLAN.md](MAPVOTE_PLAN.md), step 2.

1. Download uSource from GitHub (Code > Download ZIP) and unzip it into `Assets/uSource` in your project.
2. In Unity, select `Assets/uSource/uSource.asmdef` and tick **Allow 'unsafe' Code**, then **Apply**
   (its model reader needs it).
3. uSource doesn't state a license: use it for your own world and don't redistribute it.

## 5. Get the maps

Download the maps from GameBanana and unzip them; you need the `.bsp` files.

| Map | Where |
|---|---|
| bhop_japan (tmontana) | [gamebanana.com/mods/125304](https://gamebanana.com/mods/125304) |
| bhop_kitsune | [gamebanana.com/mods/126424](https://gamebanana.com/mods/126424) |
| bhop_eazy_v2, bhop_arcane_v1, bhop_badges | links added when they're imported (step 4 of the plan) |

The maps belong to their authors. For a **public** world, ask each author for permission first and credit them
(the vote screen shows the author's name).

## 6. Import a map

Each map needs two parts: the visuals (uSource) and the gameplay part (SourceMaps).

### 6a. Visuals with uSource (coming soon: a one-click version)
For now, by hand: open uSource's window (its menu in Unity), set **Root path** to your CS:S folder
(e.g. `C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Source`), mods `cstrike` and `hl2`,
**Unit scale 0.01905** (important: the same scale as the movement), tick saving assets to the project, put the
`.bsp` into `cstrike\maps` and load it by name.

![bhop_japan converted with uSource (white: stock textures missing on the build machine, which has no CS:S)](images/usource_bhop_japan.png)

### 6b. Gameplay with SourceMaps
1. **Tools > Source Maps > Import BSP...** and pick the same `.bsp`.
2. You get a new object named after the map with:
   - **Collision**: every solid surface of the map, including invisible walls (clip brushes).
   - **Entities**: one marker per map entity (triggers, boosters, buttons, ...) that keeps all its settings.
   - Working **fall teleports** (`trigger_teleport`).
3. Put the uSource visuals under the same object (drag uSource's map object onto it) so they move together.
4. Every map must sit somewhere else in the world: move each map object far apart (e.g. 1000 m along X).
   Maps are up to ~600 m across.

## 7. Add the map to the rotation

1. Select the map object, **Tools > Source Maps > Add Selected Map To Rotation**. This adds **Source Map Info**
   and a **Spawn** at the map's first player start.
2. On **Source Map Info**, fill in **Author** and drag a **Thumbnail** image (any texture; coming soon: rendered
   automatically). **Time Limit Minutes** overrides the lobby's setting for this map (0 = use the lobby's).
3. Check the **Spawn** child: it's where players arrive. Move/rotate it if needed.

## 8. Create the lobby

**Tools > Source Maps > Create Lobby** builds the lobby at the scene origin: a floor, the spawn, the vote board,
owner controls, and a small panel next to each map's spawn. It uses every map added in step 7 and moves the VRC
World spawn into the lobby. Run it again whenever you add a map (it replaces the old lobby).

![The vote board (test maps)](images/lobby_board.png)

Settings on **SourceMapManager** (under "SourceMaps Lobby"):

| Setting | Default | What it does |
|---|---|---|
| Mode | Vote | **Vote**: everyone votes, the whole instance plays the winner. **Choose**: each player picks a map and goes alone |
| Choices | 5 | Maps offered per vote (2-6), picked at random from all maps; the map just played isn't offered |
| Vote Seconds | 60 | Timer from the first vote to the result |
| Rtv Ratio | 0.6 | Share of players needed to "rock the vote" (start a new vote) |
| Time Limit Minutes | 30 | Then a new vote starts, with "extend" offered. **0 = rock the vote only** |
| Extend Minutes | 15 | Added when "extend" wins or the owner presses "+ time" |

## 9. Movement and timer

1. Drag `Assets/SourceMovement/SourceMovement.prefab` into the scene (Source movement, auto bhop button).
2. Drag `Assets/SourceTimer/SourceTimer.prefab` in for the run timer and leaderboards.
3. Timer zones per map: coming soon (bhop maps usually have no start/end zones in the file, they're placed by hand).

## 10. Test in Unity

Press **Play**. ClientSim (VRChat's simulator) starts you in the lobby. Click a map's thumbnail to vote
(desktop: look at it and left-click; the timer starts with the first vote). In Play mode you are the instance
owner, so you also see the owner buttons.

## 11. Upload

1. **VRChat SDK > Show Control Panel**, sign in, **Builder** tab.
2. Platform: **Windows** only (the maps are too big for Quest/Android).
3. **Build and Upload**. Big maps make big worlds: check the size it reports.

## 12. Playing it in VRChat

- **Lobby board**: press a map to vote (press another to change your vote). The vote ends 60 s after the first
  vote; ties are broken at random. Everyone is teleported to the winner.
- **In a map**: the panel next to the spawn shows the time left and the rock-the-vote count. **Rock the vote**
  (press again to take it back): at 60% of players a new vote starts in the lobby. **Back to lobby** takes only
  you back; **Rejoin map** on the lobby board takes you back in.
- **Time limit**: when it runs out everyone goes to the lobby for a vote that also offers "Extend".
- **Instance owner** (the master in public/group instances, which have no owner) sees red buttons: **Lock / unlock
  vote** (stops the timer; only Start ends it), **Start now**, **Force a map** (then press a map), **Everyone to
  lobby**, and **+ time** in maps.

## Troubleshooting

| Problem | Fix |
|---|---|
| Players fall through a map | Was **Import BSP** run (step 6b)? The visuals from uSource have no collision |
| Map looks white/pink | uSource's root path must point at your CS:S folder; pink = shader problem, re-import |
| Two maps overlap | Move the map objects apart (step 6b.4) |
| Owner buttons don't show | Only the instance owner sees them; in public/group instances, the master |
