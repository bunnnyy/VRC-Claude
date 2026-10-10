# Handoff: SourceMaps session (2026-10-10)

For the next session working on SourceMaps (map import, rotation, sample world). Read `docs/MAPVOTE_PLAN.md`
(answers, design, Progress steps 1-4), `docs/GUIDE.md` (user guide, keep it updated), `docs/SETTINGS.md`
(Inspector reference, owned by the movement session; update it when you add/rename SourceMaps/SourceTimer fields).

## State
- Steps 1-4 done (see the plan's Progress). Branch `claude/vrchat-source-movement-mn0xl0`, shared with the
  **movement session** (`session_01Fy3JhzJ1Bebcs6vM4FPYaR`, SourceMovement): always `git pull --no-rebase` before
  pushing; tell it (send_message) before changing shared files (SourceTimer, Tests/UnityPlay/run.sh,
  PlayTestBootstrap.cs, Tests/Sim shims). It owns SourceMovement; don't edit SourceMovement.
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

## Setting up a fresh container
```
# .NET 8 (dotnet-install.sh --channel 8.0 --install-dir /opt/dotnet; ln -s /opt/dotnet/dotnet /usr/local/bin/)
export DOTNET_ROOT=/opt/dotnet PATH="$PATH:$HOME/.dotnet/tools"
dotnet tool install --global vrchat.vpm.cli
apt-get install -y 7zip unar            # map archives (rar needs unar)
# Unity 2022.3.22f1 into /opt/unity (URL in MAPVOTE_PLAN.md "Context"), license:
/opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client --activate-ulf --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
Tests/Bsp/get_maps.sh                   # the 5 maps from GameBanana
Tests/Converters/compare.sh Tests/Bsp/.cache/maps/bhop_japan.bsp   # downloads uSource (needed by run.sh sample)
```

## Tests
| Command | What | Last result |
|---|---|---|
| `dotnet run --project Tests/Bsp` | BSP reader/collision on all 5 maps + booster parsing | 74/74 |
| `dotnet run --project Tests/Sim` | movement + timer simulation (incl. course boards, saved bests) | all passed |
| `Tests/UdonCompile/compile.sh` | real UdonSharp compile + editor scripts | OK |
| `Tests/UnityPlay/run.sh vote` | rotation, owner changes, practice courses (ClientSim) | 44/44 |
| `Tests/UnityPlay/run.sh map <bsp>` | one map: stand, reachable teleports, bhop/surf runs (SM_ONLY=runs: runs only) | japan/eazy/badges all passed |
| `Tests/UnityPlay/run.sh sample` | whole sample world, per map (needs uSource, zones downloaded) | 38/40 (2 drop-probe teleports) |
| `Tests/UnityPlay/run.sh 90` | movement suite (movement session's) | all passed |

## Lessons
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
