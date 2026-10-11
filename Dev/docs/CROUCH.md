# Ducking

How CS:S ducks and how `SourceMovement.cs` does it. Numbers are CS:S's (`cs_gamerules.cpp` view vectors,
`cs_gamemovement.cpp`, `gamemovement.cpp`). Back to the [README](../../README.md).

## Contents

1. [CS:S values](#1-css-values)
2. [How it works](#2-how-it-works)
3. [VRChat's player capsule](#3-vrchats-player-capsule)
4. [Tests](#4-tests)

---

## 1. CS:S values

| | CS:S | In SourceMovement |
|---|---|---|
| Standing hull | 32 x 32 x 62, eyes at 64 | `hullHeight` 62 |
| Ducked hull | 32 x 32 x 45, eyes at 47 | `duckHullHeight` 45 |
| Ducked ground speed | 0.34 x | `DuckSpeedModifier` |
| Time to duck / stand up on the ground | 0.4 s / 0.2 s | `TimeToDuck`, `TimeToUnduck` |
| In the air | Instant, origin up by half the height difference (8.5) | Same |

## 2. How it works

| Part | Behaviour |
|---|---|
| `Duck(dt)` | Runs first in `PlayerMove`. On the ground the hull shrinks from the top after 0.4 s. In the air it shrinks at once and the origin moves up 8.5 units (legs pulled up), so a crouch jump reaches 57 + 8.5 |
| Standing up | Needs room: the standing hull is checked first (in the air, downwards). Under a ceiling you stay ducked |
| Wish speed | × 0.34 while ducked on the ground; friction and air acceleration unchanged |
| Input | `duckKey` (Left Ctrl) held, or the head below `duckHeadFraction` (0.75) of eye height: VRChat's crouch (C) or a real crouch in VR. Udon can't read VRChat's crouch key, but it lowers the head |

## 3. VRChat's player capsule

| Problem | Answer |
|---|---|
| The capsule is ~84 units tall and doesn't shrink when crouching (ClientSim only lowers the head to 1.0 m), so low ceilings stop it where the Source hull fits | `SourceMovement.hullOnly` lists map roots whose solid colliders move to the **Walkthrough** layer (17) while Source movement is on: players pass through it, `Physics.BoxCast` still hits it (it's in `collisionLayers`) |
| Triggers must still touch the player | Objects that also have a trigger collider keep their layer (a layer is per object) |
| Colliders not in `hullOnly` | The resync check lets the capsule lag up to 40 units below the simulation (`CapsuleSlack`) |

## 4. Tests

| Suite | Checks |
|---|---|
| `Dev/Tests/Shared/MovementTests.cs` (sim and Udon VM) | Duck timing, 85 u/s crouch walk, crouching in VRChat (head low) ducks, standing fits a 64 gap but not 60, ducked fits 50 and stays ducked until there's room, a crouch jump lands on a 62 ledge where a plain jump doesn't |
| Unity play test | The same with ClientSim's crouch on the test map's `DuckGap` and `Ledge62` |
