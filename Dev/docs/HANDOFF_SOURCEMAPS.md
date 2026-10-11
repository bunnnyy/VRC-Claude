# Handoff: SourceMaps session (2026-10-11)

For the next session working on SourceMaps (map import, rotation, sample world). Read `Dev/docs/MAPVOTE_PLAN.md`
(answers, design, Progress steps 1-4), `docs/GUIDE.md` (user guide, keep it updated), `docs/SETTINGS.md`
(Inspector reference, owned by the movement session; update it when you add/rename SourceMaps/SourceTimer fields).

## State
- Steps 1-4 done (see the plan's Progress). Branch `claude/vrchat-source-movement-mn0xl0`, shared with the
  **movement session** (`session_01Fy3JhzJ1Bebcs6vM4FPYaR`, SourceMovement): always `git pull --no-rebase` before
  pushing; tell it (send_message) before changing shared files (SourceTimer, Dev/Tests/UnityPlay/run.sh,
  PlayTestBootstrap.cs, Dev/Tests/Sim shims). It owns SourceMovement; don't edit SourceMovement.
- Unity license: activations share one machine id across containers. Don't return it (`--return-ulf`) while the
  other session may use Unity; the last session to finish returns it, and tell the user.

## Step 5 done (2026-10-10, see the plan's Progress)
Lightmaps from the BSP (`SourceMaps/Lightmapped`), collision without faces where brushes touch, bhop/surf runs in
`run.sh map`, the arcane updraft teleport explained. Open: arcane's 3 out-of-bounds `*_stop` teleports in the single-map
test, kitsune's 2 u diagonal lip stop (movement session, step-up), lightmap atlas edge lines.
Scratch tool used for geometry questions (not in the repo): a console project compiling `Assets/SourceMaps/Editor/Bsp/*.cs`
that prints brush faces/entities near a point (remember brush entities are stored relative to their origin).

## Step 6 done (bhop blocks, prop lighting; see the plan)
New Udon scripts need their program assets committed (`.asset` + `.meta`, made by `SourceMapImporter.EnsureProgramAssets`
in the test project): run.sh copies `Assets/SourceMaps` fresh, so assets made only in the test project vanish and the
scene's behaviours lose their program. In batch mode the importer now recompiles U# itself.
VRChat's capsule floats ~6 units above the floor: thin triggers need the 8-unit raise; anything timing-sensitive
(bhop blocks) tests touches with Source's box (`Physics.OverlapBox`, null entries = VRChat-hidden colliders).

## Step 7 done (breakable glass, by the movement session; see the plan)
Upright breakables one knife hit breaks (`BspMechanics.BreaksOnApproach`) get `SourceMapBreakable` on a "Break" child:
its trigger (256 u round the glass) switches on a per-frame check of CS:S's box against knife reach (48 u, plus a step
and a frame of travel), which switches off the collider and visuals, locally, until the player leaves the instance.
`run.sh map` runs the glass test first (it stays broken); `SM_ONLY=glass` runs only that; `run.sh sample` checks the
linked model goes and it stays broken after Back to lobby / Rejoin map.

## Setting up a fresh container
```
# .NET 8 (dotnet-install.sh --channel 8.0 --install-dir /opt/dotnet; ln -s /opt/dotnet/dotnet /usr/local/bin/)
export DOTNET_ROOT=/opt/dotnet PATH="$PATH:$HOME/.dotnet/tools"
dotnet tool install --global vrchat.vpm.cli
apt-get install -y 7zip unar            # map archives (rar needs unar)
# Unity 2022.3.22f1 into /opt/unity (URL in MAPVOTE_PLAN.md "Context"), license:
/opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-ulf --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
Dev/Tests/Bsp/get_maps.sh                   # the 6 maps from GameBanana (incl. bhop_monster_jam)
Dev/Tests/Converters/compare.sh Dev/Tests/Bsp/.cache/maps/bhop_japan.bsp   # downloads uSource (needed by run.sh sample)
# Optional: CS:S + HL2 stock content (~6 GB, git-ignored cache) as a "CS:S folder" for tests (env SM_CSS): stock
# textures, models, sounds and skies (HL2 skies like sky_day01_01 and HL2 sounds are in the hl2 folder)
c=Dev/Tests/UnityPlay/.cache; mkdir -p $c/css
for r in css_content:cstrike hl2_ep2_content:hl2; do repo=${r%%:*}; dir=$c/${r##*:}_content
  git clone --depth 1 --filter=blob:none --no-checkout https://github.com/bouletmarc/$repo $dir
  git -C $dir sparse-checkout set --no-cone /materials/ /models/ /sound/ && git -C $dir checkout master
  ln -sfn ../${r##*:}_content $c/css/${r##*:}; done          # SM_CSS=$PWD/$c/css
```

## Tests
| Command | What | Last result |
|---|---|---|
| `dotnet run --project Dev/Tests/Bsp` | BSP reader/collision on all 6 maps + booster parsing + breakable glass rule | 98/98 |
| `dotnet run --project Dev/Tests/Sim` | movement + timer simulation (incl. course boards, saved bests) | all passed |
| `Dev/Tests/UdonCompile/compile.sh` | real UdonSharp compile + editor scripts | OK |
| `Dev/Tests/UnityPlay/run.sh vote` | rotation, owner changes, practice courses (ClientSim) | all passed |
| `Dev/Tests/UnityPlay/run.sh map <bsp>` | one map: breakable glass, stand, reachable teleports, bhop/surf runs (SM_ONLY=runs or glass: only that) | eazy all passed; monster_jam all but one pyramid surf (see the plan) |
| `SM_CSS=... Dev/Tests/UnityPlay/run.sh sample` | whole sample world (6 maps), per map incl. its sky and sounds (needs uSource, zones downloaded) | 63/65 (2 drop-probe teleports) |
| `ROUTE_FILE=Dev/Tests/UnityPlay/Routes/bhop_monster_jam.txt ROUTE_SECTION=n run.sh route <bsp> <zones>` | the route bot on one monster_jam stage | r, o (2), b (3), tower: done; c: start to c8 |
| `SM_CSS=... Unity -executeMethod LadderDemoBootstrap.Run -smRecord dir`, then `make_video.sh dir out.mp4` | first-person ladder clip on the test map (CS:S ladder model with SM_CSS) | recorded |
| `SM_CSS=... Unity -executeMethod LightingShots.Run -bsp map -smLightmaps on` | screenshots of an imported map (stock textures with SM_CSS) | eazy textured |
| `Dev/Tests/UnityPlay/run.sh 90` | movement suite (movement session's) | all passed |

## Route files (another map for RouteRunner)
`ROUTE_FILE=Dev/Tests/UnityPlay/Routes/<map>.txt [ROUTE_SECTION=n] run.sh route <bsp> <zones.json> [outdir]`. A route
file has the end zone and one `section z` per stage (from the teleport that starts it to its exit) with `x y [z]` points
(Source units; z = the floor height there, landing spots are looked for within 300 units of it). Write the lines with
`Dev/Tools/MapView` (`dotnet run -- route map.bsp zlo zhi sx sy ex ey [exitRadius]` searches a path over the safe
floors; the image mode draws a stage with its teleports), then check each stage with ROUTE_SECTION: the log's
"plan section N: no way on" shows stretches the planner can't continue from, and "FAIL section N: teleported back from"
where the bot falls. bhop_monster_jam's file has all 16 stages in Carmac's order; see the plan for what runs.
`run.sh route` rebuilds `Assets/RouteTest.unity` for the map it is given: recordings made right after a route run on
another map use that map's scene, so rebuild (or check the log's map name) before recording.

## Lessons
- Physics.RaycastAll returns one hit per collider: the map's world is one MeshCollider, so a roof hides the floors
  under it. Cast again from under each hit (RouteRunner's Surfaces for route files).
- Old vbsp maps (bhop_monster_jam) have no ambient index lumps: one light cube per leaf (BspFile reads both).
- uSource's material shaders decide what gets the lightmaps (`LightmappedShaders`): `$detail` surfaces come as
  USource/DetailGeneric, `$translucent` as TranslucentGeneric (both were left to Unity's lights: black, with Unity's
  shadows). Water (Water shader or %compilewater) has no lightmap: SourceMaps/Water. Sky faces are uSource's hidden
  TOOLS/TOOLSSKYBOX meshes: shown with SourceMaps/Sky (a cubemap of the map's sky). Check with
  `SM_SHOT_DESTS=c1,r1,... LightingShots` (screenshots, no run) and `SM_SHOT_MATERIALS=<dest>` (what's drawn there).
- VRChat's capsule (0.2 m) is thinner than Source's 32 unit hull: triggers are widened by the difference
  (`TriggerGrow`), and hull checks against them (SourceMapBlocks, the route bot) take it off again.
- uSource drops its file providers after LoadMap: SourceMapVisuals opens them again (CS:S folder, VPKs, the map's
  pakfile) to read sounds and the sky.
- uSource adds MeshColliders to every brush/displacement mesh: SourceMapVisuals removes them (the importer's
  collision is the map's; only solid props get colliders back).
- uSource: needs `allowUnsafeCode` in its asmdef; clears the previous map on load (detach `BSP_WorldSpawn`);
  saves materials without folder names; skinned props need baked colliders; no lights imported.
- VRChat respawns below `RespawnHeightY` (default -100 m): Create Lobby lowers it below the deepest map.
- Boards inside switched-off maps can't sync: course boards live in the lobby.
- ClientSim persistence needs the `VRC_ENABLE_PLAYER_PERSISTENCE` define (SDK sets it only in an interactive editor).
- `stream.Position += r.ReadInt32()` reads Position before the read: split it.
- Collision is a hollow mesh: overlap tests (CheckBox) don't see a point inside a solid block; use a ray with back
  faces on (`InsideSolid` in the runners). BoxCasts ignore a mesh they start touching.
- `pgrep -f "run.sh map"` in a wait loop matches its own shell: wait on the background task instead.
- How the user works: iPhone, short check-ins with numbers at every stage, simplest code, Source/CS:S behaviour,
  honest about real Unity vs simulated, ask before big design changes, keep GUIDE.md updated (photos welcome).
