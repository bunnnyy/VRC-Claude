# Source Movement for VRChat

UdonSharp prefab that recreates Source engine movement (CS:S / HL2 / GMod) in VRChat worlds:
bunny hopping, air strafing and surfing, using a direct port of Source SDK 2013 `gamemovement.cpp`.

Status: **work in progress**. Play-tested in Unity 2022.3.22f1 with VRChat's ClientSim (walking, bhop,
air strafing, surfing on real PhysX colliders, teleports, HUD; see Tests). Not yet tested in the VRChat
client itself.

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

### Source maps (`Assets/SourceMaps`, independent of the movement and timer) — work in progress
Imports the gameplay side of a CS:S map, for visuals from a converter (uSource/USource, step 2 of
[the plan](docs/MAPVOTE_PLAN.md)). **Tools > Source Maps > Import BSP...** reads the `.bsp` directly and builds:
- **Collision**: every player-solid world brush, including invisible `playerclip`/`clip` brushes, plus
  displacements, as one MeshCollider. Solid brush entities (`func_wall`, `func_door`, ...) get their own.
- **Entity markers**: one `SourceEntity` per BSP entity (triggers, boosters, buttons, props, lights...) keeping
  classname, targetname, every keyvalue and output, and the brush volume. They do nothing by themselves.
- **Teleports**: `trigger_teleport`s work right away (`SourceMapTeleport`, like Source: you face the
  destination's direction and stop). Teleports with a filter (on bhop maps usually "bhop block" teleports that
  fire when you stand on a platform too long) stay markers for now; the movement script will implement them.
  Teleport triggers are made 8 units thicker on top: Source touches them with a flat-bottomed box, VRChat's
  player is a rounded capsule hovering a few cm above the floor, so thin "don't touch the floor" triggers
  would otherwise never fire.
Same axes and scale as uSource (1 unit = 0.01905 m), so the converter's visuals line up. Meshes are saved to
`Assets/SourceMapsImported/<map>/`. Not imported yet: static prop collision (comes with the converter step).

### Crouching
Not built yet. The design is in [docs/CROUCH.md](docs/CROUCH.md).

## How it works

- VRChat's own walk/run/jump/gravity are set to 0 while active.
- Every frame the script runs fixed 100 tick Source ticks on its own axis-aligned box hull
  (32 x 72 Source units, like CS:S), swept with `Physics.BoxCast`.
- The real player is moved with `SetVelocity` so they land where the simulation says.
- Everything is in Source units (1 unit = 0.01905 m), so imported Source maps feel the same.

## Tests

Two ways to run the movement tests (expected numbers come from Source's own maths):

```
dotnet run --project Tests/Sim              # C# scripts against small Unity/VRChat stand-ins (+ timer tests)
UDON_TEST=1 Tests/UdonCompile/compile.sh    # real UdonSharp compile, then the Udon program in the Udon VM
Tests/UdonCompile/compile.sh                # real UdonSharp compile + editor script compile only
```

`Tests/UdonCompile` downloads the VRChat Worlds SDK 3.10.5, Unity 2022.3.22f1's managed DLLs and a few
Unity packages into a git-ignored cache (first run takes a few minutes), builds VRChat's UdonSharp
compiler from the SDK and runs its full pipeline (Roslyn, bind, emit, Udon assembly) on every script in
`Assets/`, failing on anything Udon doesn't support. With `UDON_TEST=1` it then runs the compiled
SourceMovement program in VRChat's Udon VM. Every extern goes through VRChat's real Udon wrapper, except
the Unity-native ones (Physics.BoxCast, input, time, euler angles), which the test world answers.

Source maps:

```
Tests/Bsp/get_maps.sh                          # downloads the test maps from GameBanana (git-ignored)
dotnet run --project Tests/Bsp                 # BSP reader + collision checks on the real maps
Tests/UnityPlay/run.sh map Tests/Bsp/.cache/maps/bhop_japan.bsp   # import in Unity, play-test with ClientSim
```

What this can't cover: Unity's real PhysX collision and how the player controller responds to
`SetVelocity`. For that there is a play test in a real Unity editor:

```
Tests/UnityPlay/run.sh            # needs UNITY=<path to Editor/Unity>, an activated license and vpm
```

It creates a VRChat world project in `Tests/UnityPlay/.cache` with `vpm`, the command-line version of the
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
`Unity -batchmode -quit -projectPath Tests/UnityPlay/.cache/Project -executeMethod PlayTestBootstrap.ExportPackage -smPackage $PWD/SourceMovement.unitypackage`

ClientSim's player is not the VRChat client's, so the last step is still a test in VRChat itself.
