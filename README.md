# Source Movement for VRChat

UdonSharp prefab that recreates Source engine movement (CS:S / HL2 / GMod) in VRChat worlds:
bunny hopping, air strafing and surfing, using a direct port of Source SDK 2013 `gamemovement.cpp`.

Status: **work in progress**. Not yet tested inside Unity or the VRChat client, but compiled with
VRChat's real UdonSharp compiler and run in VRChat's real Udon VM (see Tests).

## Using it

1. In a VRChat world project (VRChat Creator Companion, Worlds SDK 3.10.5+, Unity 2022.3.22f1), copy the
   `Assets/SourceMovement` and `Assets/SourceTimer` folders into your project's `Assets`.
2. Run **Tools > Source Movement > Create Prefabs** (or **Build Test Scene** for a ready-made test map
   with a bhop lane, a surf ramp, step tests, timer zones and a leaderboard).
3. Drop `SourceMovement.prefab` into your scene. Add `SourceTimer.prefab` if you want timing.

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

What this can't cover: Unity's real PhysX collision and how the VRChat client's player controller
responds to `SetVelocity`. Those need a Unity / VRChat play test.
