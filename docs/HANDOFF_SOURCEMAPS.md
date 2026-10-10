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

## Next (step 5, user approved)
1. Lighting: try uSource's `ParseLightmaps` (the maps' own Source lightmaps) in `SourceMapVisuals`; compare
   screenshots (bhop_arcane_v1's start room is black without lighting). If it doesn't work, document baking.
2. A real bhop and surf stretch on the imported maps with SourceMovement in ClientSim (hold W + jump, strafe),
   checking no stalls on the collision mesh (the movement session fixed mesh triangle-seam stops: verify on maps).
3. The open arcane miss: in the sample world one sampled teleport (huge 54x88x47 m out-of-bounds trigger to
   `bonus_stop`, hammerid 423175 area) doesn't fire when the player falls through it. Single-map arcane test:
   stand 185/185, teleports 20/27 tested, 118 not reachable by the test's drop-in probe. Find out why.
4. Final docs (guide, README section, plan), rebuild `SourceMovement.unitypackage` if SourceTimer changed
   (`PlayTestBootstrap.ExportPackage`, then check every entry matches the repo file), return the license if last.

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
| `dotnet run --project Tests/Bsp` | BSP reader/collision on all 5 maps + booster parsing | 56/56 |
| `dotnet run --project Tests/Sim` | movement + timer simulation (incl. course boards, saved bests) | all passed |
| `Tests/UdonCompile/compile.sh` | real UdonSharp compile + editor scripts | OK |
| `Tests/UnityPlay/run.sh vote` | rotation, owner changes, practice courses (ClientSim) | 44/44 |
| `Tests/UnityPlay/run.sh map <bsp>` | one map: stand + every reachable teleport | japan 40/40 + 55/55 |
| `Tests/UnityPlay/run.sh sample` | whole sample world, per map (needs uSource, zones downloaded) | 34/35 |
| `Tests/UnityPlay/run.sh 90` | movement suite (movement session's) | all passed |

## Lessons
- uSource: needs `allowUnsafeCode` in its asmdef; clears the previous map on load (detach `BSP_WorldSpawn`);
  saves materials without folder names; skinned props need baked colliders; no lights imported.
- VRChat respawns below `RespawnHeightY` (default -100 m): Create Lobby lowers it below the deepest map.
- Boards inside switched-off maps can't sync: course boards live in the lobby.
- ClientSim persistence needs the `VRC_ENABLE_PLAYER_PERSISTENCE` define (SDK sets it only in an interactive editor).
- `stream.Position += r.ReadInt32()` reads Position before the read: split it.
- How the user works: iPhone, short check-ins with numbers at every stage, simplest code, Source/CS:S behaviour,
  honest about real Unity vs simulated, ask before big design changes, keep GUIDE.md updated (photos welcome).
