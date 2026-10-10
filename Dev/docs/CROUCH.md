# Crouching (planned, not built yet)

How Source does it, and how it would slot into `SourceMovement.cs` later.

## What Source does

| | CS:S | HL2 / GMod |
|---|---|---|
| Standing hull | 32 x 32 x 72 | 32 x 32 x 72 |
| Crouched hull | 32 x 32 x 54 | 32 x 32 x 36 |
| Crouched speed | 0.34 x | 0.33 x |
| Time to crouch | ~0.2 s (view lowers smoothly) | 0.4 s |

- **On the ground** the hull shrinks from the top. The feet stay put and the view lowers.
- **In the air** the hull shrinks from the bottom: Source lifts the origin by the height difference, so
  your legs pull up. That's why crouch-jumping clears higher ledges (about 57 + 18 units), and it's the
  standard way to land on things in bhop and surf maps.
- **Standing back up** needs room. Source traces the standing hull first and stays crouched if
  something is overhead. In the air it un-crouches downward, so it needs room below.
- **Jumping while crouched** sets the vertical velocity instead of adding to it (`CheckJumpButton`).
- Crouched movement scales the wish speed (forward and side move), not friction or air acceleration,
  so air strafing works the same crouched.

## How it would fit in

1. **Input.** Udon has no crouch input event.
   - Desktop: a key field (default `LeftControl`) read with `Input.GetKey`.
   - VR: a controller button, or detect a real-life crouch from head height against the standing eye height.
2. **State.** Add `ducked` (bool) and a current hull height. `TraceHull` already uses `hullHalf` and
   `hullCenter`, so those are recomputed from the current height and nothing else needs to change.
3. **A `Duck(dt)` step** at the start of `PlayerMove`:
   - Pressed: shrink. In the air, also move `origin` up by the difference.
   - Released: trace the standing hull (downward when in the air). Grow only if clear.
4. **`GetWishVelocity`**: multiply by 0.34 while crouched and on the ground.
5. **`CheckJumpButton`**: `velocity.y = jumpImpulse` instead of `+=` while crouched.
6. **Step-up and surf need no changes.** `StepMove` and `TryPlayerMove` just use the current hull.

## VRChat limitations

- Udon can't force the avatar into a crouch pose. Desktop players can still crouch with VRChat's own
  key (C), which only changes the avatar pose, so the crouch key above should be a different key.
- The VRChat player capsule doesn't shrink. A crouched Source hull could fit under something the
  real capsule can't, and then the player gets blocked. The movement script already re-syncs when
  that happens, but low tunnels would feel sticky. Keeping crouch-only gaps out of maps avoids it.

Expected size: about 40 lines in `SourceMovement.cs`, plus tests for crouch-jump height, the speed
scale and the "can't stand up under a ceiling" case.
