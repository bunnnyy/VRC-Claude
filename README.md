# Source Movement for VRChat

UdonSharp prefab that recreates Source engine movement (CS:S / HL2 / GMod) in VRChat worlds:
bunny hopping, air strafing and surfing, using a direct port of Source SDK 2013 `gamemovement.cpp`.

Status: **work in progress**. The movement code is tested in a headless C# simulation only so far,
not in Unity or VRChat yet.

## How it works

- VRChat's own walk/run/jump/gravity are set to 0 while active.
- Every frame the script runs fixed 100 tick Source ticks on its own axis-aligned box hull
  (32 x 72 Source units, like CS:S), swept with `Physics.BoxCast`.
- The real player is moved with `SetVelocity` so they land where the simulation says.
- Everything is in Source units (1 unit = 0.01905 m), so imported Source maps feel the same.
- Input comes from VRChat's `InputMoveHorizontal/Vertical` and `InputJump` events, and the view
  direction from head tracking, so keyboard/mouse and VR controllers both work.

## Tests

```
dotnet run --project Tests/Sim
```

The simulation compiles the real scripts in `Assets/` against small Unity/VRChat stand-ins
(`Tests/Sim/Shims`) with an exact swept-box collision world, then checks the movement against
numbers worked out from Source's own maths.
