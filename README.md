# Source Movement for VRChat

UdonSharp prefab that recreates Source engine movement (CS:S / HL2 / GMod) in VRChat worlds:
bunny hopping, air strafing and surfing, using a direct port of Source SDK 2013 `gamemovement.cpp`.

Status: **work in progress**. Not yet tested inside Unity or the VRChat client, but compiled with
VRChat's real UdonSharp compiler and run in VRChat's real Udon VM (see Tests).

## How it works

- VRChat's own walk/run/jump/gravity are set to 0 while active.
- Every frame the script runs fixed 100 tick Source ticks on its own axis-aligned box hull
  (32 x 72 Source units, like CS:S), swept with `Physics.BoxCast`.
- The real player is moved with `SetVelocity` so they land where the simulation says.
- Everything is in Source units (1 unit = 0.01905 m), so imported Source maps feel the same.
- Input comes from VRChat's `InputMoveHorizontal/Vertical` and `InputJump` events, and the view
  direction from head tracking, so keyboard/mouse and VR controllers both work.

## Tests

Two ways to run the same 25 movement tests (expected numbers come from Source's own maths):

```
dotnet run --project Tests/Sim              # the C# script, against small Unity/VRChat stand-ins
UDON_TEST=1 Tests/UdonCompile/compile.sh    # real UdonSharp compile, then the Udon program in the Udon VM
Tests/UdonCompile/compile.sh                # real UdonSharp compile only
```

`Tests/UdonCompile` downloads the VRChat Worlds SDK 3.10.5, Unity 2022.3.22f1's managed DLLs and a few
Unity packages into a git-ignored cache (first run takes a few minutes), builds VRChat's UdonSharp
compiler from the SDK and runs its full pipeline (Roslyn, bind, emit, Udon assembly) on every script in
`Assets/`, failing on anything Udon doesn't support. With `UDON_TEST=1` it then runs the compiled
SourceMovement program in VRChat's Udon VM. Every extern goes through VRChat's real Udon wrapper, except
the Unity-native ones (Physics.BoxCast, input, time, euler angles), which the test world answers.

What this can't cover: Unity's real PhysX collision and how the VRChat client's player controller
responds to `SetVelocity`. Those need a Unity / VRChat play test.
