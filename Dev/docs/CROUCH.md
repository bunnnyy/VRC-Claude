# Ducking (built)

How CS:S ducks and how `SourceMovement.cs` does it. Numbers are CS:S (`cs_gamerules.cpp` view vectors,
`cs_gamemovement.cpp`, `gamemovement.cpp`).

| | CS:S | In SourceMovement |
|---|---|---|
| Standing hull | 32 x 32 x 62, eyes at 64 | `hullHeight` 62 |
| Ducked hull | 32 x 32 x 45, eyes at 47 | `duckHullHeight` 45 |
| Ducked ground speed | 0.34 x | `DuckSpeedModifier` |
| Time to duck / stand up on the ground | 0.4 s / 0.2 s | `TimeToDuck`, `TimeToUnduck` |
| In the air | instant, origin up by half the height difference (8.5) | same |

- **`Duck(dt)`** runs first in `PlayerMove`. On the ground the hull shrinks from the top after 0.4 s. In the air
  it shrinks at once and the origin moves up 8.5 units (the legs pull up), so a crouch jump reaches 57 + 8.5.
- **Standing up needs room**: the standing hull is checked first (in the air downwards). Under a ceiling you
  stay ducked until there is room.
- **Wish speed** is scaled by 0.34 while ducked on the ground; friction and air acceleration are unchanged.
- **Input**: `duckKey` (Left Ctrl) held, or the head lower than `duckHeadFraction` (0.75) of the avatar's eye
  height: VRChat's own crouch (C on desktop) or a real crouch in VR. Udon can't read VRChat's crouch key, but
  it lowers the head.

## VRChat's player capsule

The VRChat capsule is about 84 units tall and doesn't shrink when crouching (ClientSim only lowers the head to
1.0 m). Under a ceiling lower than that it would stop the player even where the Source hull fits.
`SourceMovement.hullOnly` lists map roots whose solid colliders move to the **Walkthrough** layer (17) while
Source movement is on: VRChat's players pass through that layer, `Physics.BoxCast` still hits it (it's in
`collisionLayers`). Objects that also have a trigger collider keep their layer, because a layer is per object
and the player has to keep touching triggers (teleports, zones, a bhop block's touch box).

The resync check allows the capsule to lag up to 40 units below the simulation (`CapsuleSlack`), for
colliders that are not in `hullOnly`.

## Tests

`Dev/Tests/Shared/MovementTests.cs` (sim and Udon VM): duck timing and 85 u/s crouch walk, crouching in VRChat
(head low) ducks, the standing hull fits a 64 gap but not 60, ducked fits a 50 gap and stays ducked until
there is room, a crouch jump lands on a 62 ledge where a plain jump doesn't. The Unity play test does the
same with ClientSim's crouch on the test map's `DuckGap` and `Ledge62`.
