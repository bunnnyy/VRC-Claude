# Source Movement for VRChat

UdonSharp prefab that recreates Source engine movement (CS:S / HL2 / GMod) in VRChat worlds:
bunny hopping, air strafing and surfing, using a direct port of Source SDK 2013 `gamemovement.cpp`.

Status: **work in progress**. Play-tested in Unity 2022.3.22f1 with VRChat's ClientSim (walking, bhop,
air strafing, surfing on real PhysX colliders, teleports, HUD; see Tests below). Not yet tested in the VRChat
client itself.

- **Step-by-step setup guide: [docs/GUIDE.md](docs/GUIDE.md)** (from an empty VRChat project to an uploaded world)
- **All settings: [docs/SETTINGS.md](docs/SETTINGS.md)**

**What you need to make a world:** `Assets/` (SourceMovement, SourceTimer, SourceMaps), or `SourceMovement.unitypackage`,
plus `docs/` (guide, settings). **`Dev/`** is for developing this project only: tests and their tools (`Dev/Tests`) and
internal notes (`Dev/docs`: plans, handoffs, designs); you can ignore it.

## Using it

1. In a VRChat world project (VRChat Creator Companion, Worlds SDK 3.10.5+, Unity 2022.3.22f1), import
   `SourceMovement.unitypackage` (Assets > Import Package > Custom Package), or copy the
   `Assets/SourceMovement` and `Assets/SourceTimer` folders into your project's `Assets`.
2. Open `Assets/SourceMovement/SourceTestMap.unity` to try it: a bhop lane, a surf ramp, step tests,
   timer zones and leaderboards.
3. Drop `SourceMovement.prefab` into your scene. Add `SourceTimer.prefab` if you want timing.

The prefabs and the test map are made by **Tools > Source Movement > Create Prefabs** and
**Build Test Scene**; run those again only if you change the setup code.

### SourceMovement (the movement)
- All Source cvars are in the Inspector (bhop server defaults: `sv_airaccelerate 1000`,
  `sv_accelerate 5`, friction 4, 250 max speed, 100 tick).
- **Active On Start**: on by default. Turn it off and enable the prefab's `MovementZone (optional)`
  child (a trigger Box Collider) to have Source movement only inside that box.
- **Auto Bhop**: hold jump to bhop. Toggle at runtime with **B** (`Auto Bhop Toggle Key`).
  The scroll wheel always jumps, for manual bhopping.
- **AutoBhopButton** child: a world button (Interact) that toggles auto bhop, for VR players. Move it
  wherever you like; its label shows the current mode.
- Desktop and VR both work: movement comes from VRChat's move/jump input, direction from head yaw.
- **Ladders** work like CS:S: walk into one to grab it, W/S climb along your view (look up to go up, look
  down past 45 degrees to go down), A/D move sideways, no keys hangs on, jump pushes off at 270 u/s,
  climb speed 200 u/s. A ladder is a **trigger collider on the ladder layer** (Inspector: `Ladder Layers`,
  default layer 22) placed against the climbable face; the wall behind it is a normal collider. The test
  map has a ladder tower beside the bhop lane.
- **Water** works like CS:S: waist deep or more you swim along your view at 200 u/s (80% of max speed),
  no keys sinks at 48 u/s, jump swims up (~100 u/s), and pushing against a ledge while waist deep jumps you
  out (water jump). Ankle deep is normal walking. Water is a **trigger collider on the water layer**
  (Inspector: `Water Layers`, default Unity's Water layer 4) filling the water; the pool floor is a normal
  collider. The test map has a deep pool and a waist-deep pool with a ledge.
- **Boosters** (Source map triggers), each a script on a trigger collider. They find the object named
  `SourceMovement` themselves, or set their `movement` field:
  - `SourcePushTrigger` = `trigger_push`: `push` velocity (Source units/s). Inside, it moves you without
    becoming your speed; leaving adds it as momentum. Upward pushes lift you against gravity.
  - `SourceBoostTrigger` = bhop boosters (`AddOutput basevelocity ...`, `AddOutput gravity ...`,
    `trigger_gravity`): `addVelocity` once, and/or set a `gravityScale`, on enter or on leave.
  - Other scripts can call `SetPush(v)`, `AddVelocity(v)` and `SetGravityScale(s)` on SourceMovement.
  The test map has a booster platform: push pad, push column, launch pad, half and normal gravity pads.
- **Teleporting**: plain `VRCPlayerApi.TeleportTo` works. A jump over 64 units resets velocity, a
  smaller one keeps it. To choose, call `sourceMovement.TeleportPlayer(position, rotation, keepVelocity)`.
  VRChat keeps the player's old velocity for a moment after `TeleportTo`, so call
  `SetVelocity(Vector3.zero)` right after it (as usual in VRChat) or the player lands a few units off.
- Other scripts can read `GetSpeed()`, `GetSourceVelocity()`, `IsOnGround()` and call `SetMovementActive(bool)`.

### Speedometer (debug)
The `Speedometer` child shows horizontal speed in Source units/s in front of your view. Tick **Show**
on it in the Inspector. It's off in the prefab and on in the test scene.

### Timer, checkpoints, leaderboard (`Assets/SourceTimer`, independent of the movement)
- `TimerZone` on a trigger collider, with a type: **Start** (timer starts when you leave it), **End**,
  **Checkpoint** (sets where Reset sends you), **Reset** (back to the checkpoint, for kill floors under
  ramps) and **Restart**. Press **G** to restart.
- `Leaderboard`: synced top 10 for the instance, one best time per player. Times go to the object owner,
  so two players finishing together can't overwrite each other.
- **Auto bhop and legit bhop are separate categories.** The prefab has two boards. Set RunTimer's
  **Category Source** to your SourceMovement (the test scene does this). If auto bhop is on at any
  moment during a run, the run counts as auto bhop.

### Source maps (`Assets/SourceMaps`, independent of the movement and timer)
A CS:S bhop-server style world: imported maps, a lobby with a map vote, rock the vote and a time limit.
Step-by-step setup: **[docs/GUIDE.md](docs/GUIDE.md)**; design and test history: [Dev/docs/MAPVOTE_PLAN.md](Dev/docs/MAPVOTE_PLAN.md).
- **Import** (**Tools > Source Maps > Import BSP...**, our own BSP reader): collision from every player-solid brush
  (incl. invisible clips) and displacement, without the faces where two brushes touch (Source never collides with
  those; in a mesh their edges stop surfers); one `SourceEntity` marker per entity with all its keyvalues and outputs;
  working teleports, pushes, boosters, gravity, water and ladders for SourceMovement; **bhop blocks** like CS:S
  (name-filtered teleports and sinking `func_door` blocks: stand too long and you're sent back), switchable per map.
- **Visuals** with [uSource](https://github.com/DeadZoneLuna/uSource) (installed by you, not included): the map's own
  Source **lightmaps** on a small `SourceMaps/Lightmapped` shader (nothing to bake), static props lit from the map's
  ambient light and lights like CS:S lights models, tool surfaces removed, solid props get colliders.
- **Timer zones** from [zones-cstrike](https://github.com/srcwr/zones-cstrike) (what CS:S bhop servers use).
- **Map rotation** (`SourceMapManager`): 5 random maps per vote, 60 s timer, owner controls, rock the vote, time
  limit with extend, saved records per map. **Build Sample World** makes all of it from a folder of `.bsp` files.
Same axes and scale as uSource (1 unit = 0.01905 m).

### Ducking
As in CS:S: hold **Left Ctrl**, or crouch with VRChat's own crouch (**C** on desktop, or crouch in VR). The hull
shrinks from 62 to 45 units, ground speed drops to 34%, and ducking in the air pulls your legs up, so a crouch
jump lands on ledges up to about 65 units. Standing up waits until there is room above.
VRChat's own player capsule doesn't shrink, so put maps with low ceilings or vents in **Hull Only** on
SourceMovement: their colliders move to VRChat's Walkthrough layer while Source movement is on, and only the
Source hull collides with them ([settings](docs/SETTINGS.md)).

## How it works

- VRChat's own walk/run/jump/gravity are set to 0 while active.
- Every frame the script runs fixed 100 tick Source ticks on its own axis-aligned box hull
  (32 x 62 Source units, 45 ducked, like CS:S), swept with `Physics.BoxCast`.
- The real player is moved with `SetVelocity` so they land where the simulation says.
- Everything is in Source units (1 unit = 0.01905 m), so imported Source maps feel the same.

## Tests (in `Dev/`, for development)

Two ways to run the movement tests (expected numbers come from Source's own maths):

```
dotnet run --project Dev/Tests/Sim              # C# scripts against small Unity/VRChat stand-ins (+ timer tests)
UDON_TEST=1 Dev/Tests/UdonCompile/compile.sh    # real UdonSharp compile, then the Udon program in the Udon VM
Dev/Tests/UdonCompile/compile.sh                # real UdonSharp compile + editor script compile only
```

`Dev/Tests/UdonCompile` downloads the VRChat Worlds SDK 3.10.5, Unity 2022.3.22f1's managed DLLs and a few
Unity packages into a git-ignored cache (first run takes a few minutes), builds VRChat's UdonSharp
compiler from the SDK and runs its full pipeline (Roslyn, bind, emit, Udon assembly) on every script in
`Assets/`, failing on anything Udon doesn't support. With `UDON_TEST=1` it then runs the compiled
SourceMovement program in VRChat's Udon VM. Every extern goes through VRChat's real Udon wrapper, except
the Unity-native ones (Physics.BoxCast, input, time, euler angles), which the test world answers.

Source maps:

```
Dev/Tests/Bsp/get_maps.sh                          # downloads the test maps from GameBanana (git-ignored)
dotnet run --project Dev/Tests/Bsp                 # BSP reader + collision checks on the real maps
Dev/Tests/UnityPlay/run.sh map Dev/Tests/Bsp/.cache/maps/bhop_japan.bsp   # import in Unity, play-test with ClientSim:
                                               # stand at every destination, teleports, bhop and surf runs (no stalls)
Dev/Tests/UnityPlay/run.sh vote                    # map rotation (vote, owner changes, practice courses)
Dev/Tests/UnityPlay/run.sh sample                  # the whole sample world with uSource visuals and zones
```

A bot plays bhop_eazy_v2 with only a player's inputs (A/D strafes with the view, jump, C to duck, W to run up),
and can record it: `ROUTE_SECTION=n` starts at a section, the frames and a HUD log go to the folder given, and
`make_video.sh` turns them into an mp4 with the speed, the map timer and the keys burnt in. It plans each hop by
flying it in a model of the movement (input lag, air acceleration, the map's collision, surf faces) and checks a
hop on from where it lands; the route, red lane 1's ridge surf (built up to speed by circle strafing first) and
the zig-zag lines round glass pillars are specific to that map.

```
Dev/Tests/UnityPlay/run.sh route bhop_eazy_v2.bsp zones.json [framesdir]
SCALE=960:540 Dev/Tests/UnityPlay/make_video.sh framesdir run.mp4 "title"
```

What this can't cover: Unity's real PhysX collision and how the player controller responds to
`SetVelocity`. For that there is a play test in a real Unity editor:

```
Dev/Tests/UnityPlay/run.sh            # needs UNITY=<path to Editor/Unity>, an activated license and vpm
```

It creates a VRChat world project in `Dev/Tests/UnityPlay/.cache` with `vpm`, the command-line version of the
VRChat Creator Companion (`dotnet tool install --global vrchat.vpm.cli`), from VRChat's official World
template with Worlds SDK 3.10.5, so it has VRChat's layers and project settings. It copies
`Assets/SourceMovement` and `Assets/SourceTimer` in, builds the test map with the
**Build Test Scene** menu code, then plays it with ClientSim at 30, 90 and 144 fps, using a virtual
keyboard through ClientSim's own input path. It checks the real player against the simulation: walk speed,
stopping, bhop jump height, air strafe gain, surfing on the PhysX ramp (stays on it, keeps speed and height),
`TeleportTo` and `TeleportPlayer`, that the speedometer and timer text show, legit mode through the
world button (no pogo), and a timed run through the real trigger zones (checkpoint, reset zone, end zone,
legit leaderboard). The `.unitypackage` was also imported into an empty project and passes the same tests.

To re-export the package after changes (from the project `run.sh` made):
`Unity -batchmode -quit -projectPath Dev/Tests/UnityPlay/.cache/Project -executeMethod PlayTestBootstrap.ExportPackage -smPackage $PWD/SourceMovement.unitypackage`

ClientSim's player is not the VRChat client's, so the last step is still a test in VRChat itself.
