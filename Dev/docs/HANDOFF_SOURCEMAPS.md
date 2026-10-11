# Handoff: SourceMaps

For the next session working on SourceMaps (map import, rotation, sample world). Updated 2026-10-11.

## Contents

1. [Rules and state](#1-rules-and-state)
2. [Setting up a fresh container](#2-setting-up-a-fresh-container)
3. [Tests](#3-tests)
4. [Route files](#4-route-files)
5. [Open issues](#5-open-issues)
6. [Lessons](#6-lessons)

| Read too | Why |
|---|---|
| [MAPVOTE_PLAN.md](MAPVOTE_PLAN.md) | Design, answers, results per step (1-8) |
| [TESTING.md](TESTING.md) | Every test command |
| [docs/GUIDE.md](../../docs/GUIDE.md) | User guide: keep it updated |
| [docs/SETTINGS.md](../../docs/SETTINGS.md) | Inspector reference: update it when SourceMaps/SourceTimer fields change |

---

## 1. Rules and state

| Topic | Rule |
|---|---|
| Branch | `claude/vrchat-source-movement-mn0xl0`, shared: always `git pull --no-rebase` before pushing |
| Movement session | `session_01Fy3JhzJ1Bebcs6vM4FPYaR` owns SourceMovement: never edit it. Message it (send_message) before changing shared files: SourceTimer, `run.sh`, `PlayTestBootstrap.cs`, `RouteRunner.cs`, Sim shims, README/GUIDE/SETTINGS |
| Unity licence | One machine id across containers. Whoever finishes Unity last returns it (`--return-ulf`) and tells the user and the other session |
| Credentials | `UNITY_EMAIL` / `UNITY_PASSWORD` from the environment; never print them |
| Valve content | CS:S/HL2 files stay in the git-ignored `Dev/Tests/UnityPlay/.cache`; never commit them |
| New Udon scripts | Commit their program assets (`.asset` + `.meta`, from `SourceMapImporter.EnsureProgramAssets`): `run.sh` copies `Assets/SourceMaps` fresh |
| The user | Reads on iPhone: short check-ins with numbers, simplest code, CS:S behaviour as the reference, honest about real Unity vs simulated, asks before big design changes; screenshots over test recordings |

| Step | State |
|---|---|
| 1-7 | Done (import, converter, rotation, sample world, lightmaps, bhop blocks, glass): see the plan |
| 8 | bhop_monster_jam, sounds, skies, route bot, video: done, open items below |
| Since | Lighting like CS:S (detail/translucent lightmapped, water, sky faces, no Unity shadows), triggers widened to the CS:S hull, lobby as UI menus |

## 2. Setting up a fresh container

```
# .NET 8 (dotnet-install.sh --channel 8.0 --install-dir /opt/dotnet; ln -s /opt/dotnet/dotnet /usr/local/bin/)
export DOTNET_ROOT=/opt/dotnet PATH="$PATH:$HOME/.dotnet/tools"
dotnet tool install --global vrchat.vpm.cli
apt-get install -y 7zip unar            # map archives (rar needs unar)
# Unity 2022.3.22f1 into /opt/unity (URL in MAPVOTE_PLAN.md, Context), licence:
/opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-ulf --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
Dev/Tests/Bsp/get_maps.sh                   # the 6 maps from GameBanana
Dev/Tests/Converters/compare.sh Dev/Tests/Bsp/.cache/maps/bhop_japan.bsp   # downloads uSource (run.sh sample needs it)
# Optional: CS:S + HL2 stock content (~6 GB, git-ignored) as a "CS:S folder" for tests (env SM_CSS)
c=Dev/Tests/UnityPlay/.cache; mkdir -p $c/css
for r in css_content:cstrike hl2_ep2_content:hl2; do repo=${r%%:*}; dir=$c/${r##*:}_content
  git clone --depth 1 --filter=blob:none --no-checkout https://github.com/bouletmarc/$repo $dir
  git -C $dir sparse-checkout set --no-cone /materials/ /models/ /sound/ && git -C $dir checkout master
  ln -sfn ../${r##*:}_content $c/css/${r##*:}; done          # SM_CSS=$PWD/$c/css
```

## 3. Tests

All commands and last results: [TESTING.md](TESTING.md#1-test-suites). The ones for SourceMaps:

| Command | Last result |
|---|---|
| `dotnet run --project Dev/Tests/Bsp` | 98/98 |
| `Dev/Tests/UnityPlay/run.sh vote` | 52/52 (votes through the real UI buttons; dragged-in prefabs) |
| `Dev/Tests/UnityPlay/run.sh map <bsp>` | eazy all passed; monster_jam all but one pyramid surf |
| `SM_CSS=... Dev/Tests/UnityPlay/run.sh sample` | 63/65 (2 known drop-probe misses: arcane, japan) |
| `SM_CSS=... SM_SHOT_DESTS=... LightingShots.Run` | Screenshots; use these instead of recordings to check visuals |

## 4. Route files

`ROUTE_FILE=Dev/Tests/UnityPlay/Routes/<map>.txt [ROUTE_SECTION=n] run.sh route <bsp> <zones.json> [outdir]`

| Line | Meaning |
|---|---|
| `end x1 y1 x2 y2` | The end zone |
| `section z [pads]` | A stage, from the teleport that starts it to its exit; `z` its floor; `pads` = land on pad middles |
| `x y [z]` | Route points in Source units; `z` = floor height there (landing spots within 300 units of it) |

| # | Workflow |
|---|---|
| 1 | Draw the stage: `dotnet run --project Dev/Tools/MapView -- map.bsp out.ppm x0 y0 x1 y1 [upp zlo zhi]` |
| 2 | Path over safe floors: `... -- route map.bsp zlo zhi sx sy ex ey [exitRadius]`; bhop blocks: `... -- blocks ...`; a trigger's brushes: `... -- model map.bsp n` |
| 3 | Run one stage (`ROUTE_SECTION=n`, `-smRouteOne 1` via direct Unity) and read the log: "plan section N: no way on" = stretches the planner can't continue; "FAIL section N: teleported back from" = where the bot falls |

> [!WARNING]
> `run.sh route` rebuilds `Assets/RouteTest.unity` for the map it's given. A recording right after a route run on
> another map uses that map's scene: rebuild (or check the log's map name) first.

## 5. Open issues

| # | Issue | Where |
|---|---|---|
| 1 | Bot turn-rate easing (smoother strafing) made stage r worse on its first try (4 falls vs 1); the fix after it is untested | `RouteRunner.TurnAccel` |
| 2 | e's exit should now be reachable (triggers widened); en has a new route through the doorway; both untested with the bot | `Routes/bhop_monster_jam.txt` |
| 3 | ch is bhops over a grass bank and stepping stones (not a surf): route to rewrite | same |
| 4 | One pyramid surf in Egypt loses speed with nothing ahead | `run.sh map` monster_jam |
| 5 | Bot video `Dev/docs/media/bhop_monster_jam_bot.mp4` predates the lighting fixes | re-record |
| 6 | Drop-probe misses: arcane's out-of-bounds `*_stop` volumes, one japan teleport | the test's probe, not the triggers |
| 7 | Faint lines where two lightmapped faces meet (atlas edges) | cosmetic |

## 6. Lessons

| Area | Lesson |
|---|---|
| Raycasts | `Physics.RaycastAll` returns one hit per collider: the map is one MeshCollider, so a roof hides the floors under it. Cast again from under each hit |
| Collision | A hollow mesh: overlap tests don't see a point inside a solid block (use a ray with back faces, `InsideSolid`); BoxCasts ignore a mesh they start touching |
| Triggers | VRChat's capsule floats ~6 units up (triggers raised 8) and is 0.2 m wide vs Source's 32-unit hull (triggers widened by the difference, `TriggerGrow`; hull checks take it off again). Timing-sensitive touches (bhop blocks) use Source's box |
| BSP | Old vbsp maps (monster_jam) have no ambient index lumps: one light cube per leaf. `stream.Position += r.ReadInt32()` reads Position first: split it |
| uSource | Needs `allowUnsafeCode`; clears the previous map on load (detach `BSP_WorldSpawn`); adds MeshColliders everywhere (removed); drops its file providers after LoadMap (reopened for sounds/sky); 3D skybox off (adds a fly camera) |
| Lighting | uSource's shader decides what gets lightmaps (`LightmappedShaders`): `$detail` = DetailGeneric, `$translucent` = TranslucentGeneric. Water (Water shader or `%compilewater`) has no lightmap. Sky faces are its hidden `TOOLS/TOOLSSKYBOX` meshes |
| Menus | Canvas + VRCUiShape + a trigger BoxCollider (what the pointer raycast hits); Buttons call `SendCustomEvent("Click")`. VRChat's UnityEventFilter drops those without the `UDON` define |
| Prefabs | `Prefabs/VoteMenu`, `MapMenu` (SourceMaps) and `SourceTimer/Scoreboard` are built by code (Create Menu Prefabs / Create Scoreboard Prefab) in the test project and copied back with their `.meta`s. Unwired, they find "SourceMapManager" by name and register (`_AddScreen`). Udon objects made by `Object.Instantiate` from a MonoBehaviour aren't initialised: test drag-in by placing them at edit time |
| VRChat | Respawn below `RespawnHeightY` (Create Lobby lowers it); boards in switched-off maps can't sync (they live in the lobby); ClientSim persistence needs `VRC_ENABLE_PLAYER_PERSISTENCE` |
| Shell | `pgrep -f` / `pkill -f` match the calling shell: wait on background tasks instead, never `pkill -f xvfb-run` |
