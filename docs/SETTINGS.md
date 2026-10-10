# Settings reference

Every changeable setting of SourceMovement, SourceTimer and SourceMaps. For setting up a world step by step, see
the [setup guide](GUIDE.md).

Every setting is on a component in the Inspector (select the object, change the value). Defaults are what the
prefabs and tools create. Distances and speeds are in **Source units** (1 unit = 0.01905 m) unless a row says
metres, so values from CS:S servers and maps can be copied straight over.

## SourceMovement (`SourceMovement.prefab`, root object)

**Movement cvars** (the CS:S server variables they come from in brackets):

| Setting | Default | What it does |
|---|---|---|
| Gravity | 800 | `sv_gravity`. Higher falls faster and jumps lower |
| Accelerate | 5 | `sv_accelerate`: how fast you reach running speed on the ground |
| Air Accelerate | 1000 | `sv_airaccelerate`: how much air strafing gains. CS:S default 10, surf servers 100-150, bhop servers 1000 |
| Friction | 4 | `sv_friction`: ground friction (also water friction) |
| Stop Speed | 75 | `sv_stopspeed`: below this speed friction acts as if you were moving this fast, so you stop quickly |
| Max Speed | 250 | Running speed (CS:S: knife/pistols 250, rifles ~215-230). Swimming is 80% of this |
| Max Velocity | 3500 | `sv_maxvelocity`, a cap per axis (falling speed limit) |
| Air Speed Cap | 30 | The air wish speed cap every Source game has; it's what makes air strafing gain speed. Leave it at 30 |
| Jump Impulse | 301.99 | Jump speed. Jump height = Jump Impulse² / (2 × Gravity) = 57 units. **Change it together with Gravity** |
| Step Size | 18 | `sv_stepsize`: highest step you walk up without jumping |
| Tick Rate | 100 | Simulation ticks per second (bhop servers 100, CS:S default 66). Changes how bhop and strafing feel, as in CS:S |

**Player hull**:

| Setting | Default | What it does |
|---|---|---|
| Hull Width | 32 | Width and depth of the player's collision box (CS:S standing hull) |
| Hull Height | 72 | Its height. Crouching isn't built yet ([CROUCH.md](CROUCH.md)) |

**World**:

| Setting | Default | What it does |
|---|---|---|
| Meters Per Unit | 0.01905 | Size of one Source unit. **Must match the scale the maps were imported with** (0.01905) |
| Collision Layers | Default, Environment | Layers the player collides with. Put solid level geometry on one of these |
| Ladder Layers | 22 | Layers of ladder volumes (trigger boxes against the climbable face). Name layer 22 "Ladder" if you like |
| Water Layers | 4 (Water) | Layers of water volumes (trigger boxes filling the water) |

**Controls**:

| Setting | Default | What it does |
|---|---|---|
| Active On Start | on | Source movement from the start. Off: only inside the optional MovementZone (below) |
| Auto Bhop | on | On: hold jump to keep hopping. Off: every hop needs a new press (scroll wheel works). Runs with it on count as "auto" on the boards |
| Auto Bhop Toggle Key | B | Desktop key that switches auto bhop at runtime |

Fixed in code, the same as CS:S (not in the Inspector): forward/side speed 450, walkable slope 0.7 (steeper
than ~45.6° is a surf ramp), ladder climb 200 u/s and jump-off 270 u/s, swim up 100 u/s, sink 60 (48 after the
80% swim factor), water jump 256 u/s, eye height 64.

**Presets**:

| | Air Accelerate | Tick Rate | Auto Bhop | Notes |
|---|---|---|---|---|
| Bhop server (default) | 1000 | 100 | on | |
| Surf server | 150 | 100 | on | 100-150 is typical |
| Stock CS:S | 10 | 66 | off | Max Speed 250 (knife) |

## Children of SourceMovement

| Component (object) | Setting | Default | What it does |
|---|---|---|---|
| SourceSpeedometer (Speedometer) | Show | off (on in the test map) | Shows horizontal speed in front of your view |
| | Head Offset | 0, -0.3, 1 (metres) | Where the number floats relative to your eyes |
| | Movement, Label | set by the prefab | The SourceMovement it reads, and its text |
| SourceMovementZone (MovementZone (optional)) | (Box Collider size) | disabled object | Enable the object and turn **Active On Start** off to have Source movement only inside the box |
| AutoBhopButton (AutoBhopButton) | Movement, Label | set by the prefab | World button (Interact) that toggles auto bhop for VR players. Move it anywhere |

## Map triggers for SourceMovement (the map importer adds these)

| Component | Setting | Default | What it does |
|---|---|---|---|
| SourcePushTrigger (`trigger_push`) | Push | 0, 0, 0 | Push velocity in units/s (speed × direction). Inside, it moves you; leaving keeps it as momentum. Upward pushes lift |
| | Movement | empty | Empty = finds the object named `SourceMovement` |
| SourceBoostTrigger (boosters, `trigger_gravity`) | Add Velocity | 0, 0, 0 | Speed added once (units/s), e.g. 0, 600, 0 launches you up ~225 units |
| | Set Gravity / Gravity Scale | off / 1 | Also sets your gravity multiplier (0.5 = half). Reset on respawn |
| | On Leave | off | Fire when you leave the trigger instead of when you enter it |
| | Movement | empty | As above |

Ladders and water need no script: a trigger Box Collider on the ladder layer (22) or water layer (4).

## SourceTimer (`SourceTimer.prefab`)

**RunTimer** (RunTimer object):

| Setting | Default | What it does |
|---|---|---|
| Leaderboard | Leaderboard (Legit) | Board for normal (legit) runs |
| Category Source | SourceMovement (test map / lobby tool) | Script whose bool marks a run as the other category |
| Category Variable | autoBhop | Name of that bool. If it's on at any moment of a run, the run counts as auto bhop |
| Auto Leaderboard | Leaderboard (Auto) | Board for auto bhop runs |
| Start Point | (set by the tools) | Where Restart (G) sends you, inside the start zone |
| Label | set by the prefab | The timer text |
| Head Offset | 0, 0.25, 1 (metres) | Where the timer floats relative to your eyes |
| Restart Key | G | Desktop key that restarts the run |

**TimerZone** (a trigger Box Collider plus this):

| Setting | Default | What it does |
|---|---|---|
| Zone Type | Start | **Start**: timer starts when you leave it. **End**: finishes the run. **Checkpoint**: sets where Reset sends you. **Reset**: back to the last checkpoint (kill floors, under surf ramps). **Restart**: back to the start |
| Timer | the RunTimer | Which timer it drives |
| Respawn Point | this object | Checkpoint: where Reset sends you. Start: where Restart sends you for this course |
| Leaderboard / Auto Leaderboard | empty | Start zones only: this course's own boards. Empty = the RunTimer's boards |

**Leaderboard**:

| Setting | Default | What it does |
|---|---|---|
| Title | "Best times" | Heading on the board |
| Board | set by the prefab | The board's text |
| Save Key | empty | Saves each player's best with VRChat Persistence under this key (e.g. `bhop_japan_legit`). Empty = this instance only. Use a different key per board |

Shows the top 10, one time per player (fixed).

## SourceMaps (lobby and map rotation)

**SourceMapManager** (under "SourceMaps Lobby"): the vote settings are in the table in [the guide, step 8](GUIDE.md#8-create-the-lobby) (Choices, Vote
Seconds, Rtv Ratio, Time Limit Minutes, Extend Minutes). The rest is wiring the lobby tool fills in:

| Setting | What it is |
|---|---|
| Maps | Every map in the rotation (Add Selected Map To Rotation fills this) |
| Lobby Spawn | Where players wait and vote; keep the VRC World spawn here |
| Screens | The lobby board and the map panels it updates |

**SourceMapInfo** (on each map's object):

| Setting | Default | What it does |
|---|---|---|
| Map Name | the BSP name | Name on the vote screen |
| Author | empty | Shown on the vote screen (credit the map maker) |
| Thumbnail | rendered from the spawn | Picture on the vote screen; drag your own image over it |
| Spawn | the map's first player start | Where players arrive |
| Time Limit Minutes | 0 | This map's own time limit; 0 = the manager's |

**SourceMapTeleport** (fall respawns from `trigger_teleport`): **Destination** = where the player is sent.
**SourceEntity** (markers): read-only copies of the map entity's data (class name, name, keyvalues, outputs,
volume); changing them does nothing.

## VRChat and Unity settings that matter

| Where | Setting | Note |
|---|---|---|
| VRC World (VRC Scene Descriptor) | Respawn Height Y | Falling below this respawns you. Maps sit at different heights: keep it below the lowest map |
| Project Settings > Player | Active Input Handling | **Both** (the VRChat template's default). The B/G keys and scroll jump use the old input system |
| Project Settings > Tags and Layers | Layers 4, 11, 22 | Water, Environment (solid), Ladder: keep the colliders on the layers SourceMovement expects |
