#!/usr/bin/env bash
# Play-tests the prefab in a real Unity 2022.3.22f1 editor: builds the test map with the editor menu code,
# then runs PlayTestRunner in play mode with VRChat's ClientSim (real PhysX, ClientSim's player controller
# and input path) at each frame rate given (default 30 90 144).
# Needs an activated Unity license, UNITY pointing at the editor binary and VRChat's vpm CLI
# (dotnet tool install --global vrchat.vpm.cli). The project is created in Tests/UnityPlay/.cache (git-ignored).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
unity=${UNITY:-/opt/unity/Editor/Unity}
proj=$here/.cache/Project
logs=$here/.cache/logs
command -v vpm >/dev/null || { echo "Install vpm: dotnet tool install --global vrchat.vpm.cli"; exit 1; }
mkdir -p "$logs"

run() { # log name, Unity args...
  local log=$logs/$1.log; shift
  local x=(); command -v xvfb-run >/dev/null && x=(xvfb-run -a)
  # Time limit: a batchmode editor that hits a problem (e.g. compile errors) can wait forever.
  timeout "${UNITY_TIMEOUT:-1800}" "${x[@]}" "$unity" -batchmode -projectPath "$proj" -logFile "$log" "$@" >/dev/null 2>&1 && return 0
  grep -E "error CS|Exception|\[SMTEST\] FAIL" "$log" | head -20; return 1
}

if [ ! -d "$proj/Assets" ]; then
  echo "Creating a VRChat world project with vpm (the Creator Companion CLI)"
  mkdir -p "$(dirname "$proj")"
  vpm install templates
  vpm new Project World -p "$(dirname "$proj")"
  vpm add package com.vrchat.worlds@3.10.5 -p "$proj"
  ln -s "$here" "$proj/Assets/SourcePlayTests"
  echo "First import (several minutes)"
  run open -quit || true # a brand-new project hangs if the first launch also runs a method
fi

# The VRChat SDK adds these defines when an interactive editor opens (EnvConfig), not in batch mode; ClientSim's
# player persistence (PlayerData) only works with VRC_ENABLE_PLAYER_PERSISTENCE.
if ! grep -q "VRC_ENABLE_PLAYER_PERSISTENCE" "$proj/ProjectSettings/ProjectSettings.asset"; then
  # Only the Standalone line inside scriptingDefineSymbols (other settings have Standalone lines too).
  sed -i '/^  scriptingDefineSymbols:/,/^  [a-zA-Z]/ s|^    Standalone: \(.*\)$|    Standalone: \1;VRC_SDK_VRCSDK3;VRC_ENABLE_PLAYER_PERSISTENCE|' "$proj/ProjectSettings/ProjectSettings.asset"
fi

# The project gets a copy of the assets, so Unity never rewrites the committed files.
copy_assets() {
  for d in SourceMovement SourceTimer SourceMaps; do
    rm -rf "$proj/Assets/$d"
    cp -r "$repo/Assets/$d" "$proj/Assets/$d"
    if [ -f "$repo/Assets/$d.meta" ]; then cp "$repo/Assets/$d.meta" "$proj/Assets/"; fi
  done
}

# Map mode: run.sh map a.bsp [b.bsp ...] imports each map and play-tests it at 90 fps.
if [ "${1:-}" = "map" ]; then
  shift
  copy_assets
  status=0
  for bsp in "$@"; do
    name=$(basename "$bsp" .bsp)
    if run "import_$name" -quit -executeMethod PlayTestBootstrap.ImportMap -bsp "$(realpath "$bsp")" &&
       run "map_$name" -executeMethod PlayTestBootstrap.RunMap -smFrameRate 90; then r="ALL PASSED"; else r="FAILED"; status=1; fi
    echo "== $name: $r"
    grep -h "\[Source Maps\]" "$logs/import_$name.log" | head -1 | sed 's/^/  /'
    grep -ho "\[SMTEST\] .*" "$logs/map_$name.log" 2>/dev/null | grep -vE "^\[SMTEST\] $|ALL PASSED|FAILED$" | sed 's/^\[SMTEST\] /  /' | sort -u
  done
  exit $status
fi

# TextMeshPro's Essential Resources (default font). The editor imports them asynchronously, after a batch mode
# run has already quit, so unpack the .unitypackage (a tar of <guid>/pathname, asset, asset.meta) directly.
tmp_essentials() {
  [ -d "$proj/Assets/TextMesh Pro" ] && return 0
  local pkg; pkg=$(ls "$proj"/Library/PackageCache/com.unity.textmeshpro@*/"Package Resources/TMP Essential Resources.unitypackage" | head -1)
  python3 - "$pkg" "$proj" <<'PY'
import sys, tarfile, os
pkg, proj = sys.argv[1], sys.argv[2]
t = tarfile.open(pkg)
names = {}
for m in t.getmembers():
    guid, _, kind = m.name.lstrip('./').partition('/')
    if kind: names.setdefault(guid, {})[kind] = m
for guid, parts in names.items():
    if 'pathname' not in parts: continue
    path = t.extractfile(parts['pathname']).read().decode().splitlines()[0]
    dest = os.path.join(proj, path)
    if 'asset' in parts:
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        open(dest, 'wb').write(t.extractfile(parts['asset']).read())
    else:
        os.makedirs(dest, exist_ok=True)
    if 'asset.meta' in parts:
        open(dest + '.meta', 'wb').write(t.extractfile(parts['asset.meta']).read())
PY
}

# Vote mode: run.sh vote builds the SourceMaps test scene (6 box maps + lobby) and play-tests the map rotation.
if [ "${1:-}" = "vote" ]; then
  copy_assets
  tmp_essentials
  rm -rf "$proj/ClientSimStorage" # ClientSim keeps PlayerData between runs: start without saved times
  run program_assets -quit -executeMethod SourceMapImporter.EnsureProgramAssets
  status=0
  if run build_vote -quit -executeMethod SourceMapsSetup.BuildTestScene && run vote -executeMethod PlayTestBootstrap.RunVote; then r="ALL PASSED"; else r="FAILED"; status=1; fi
  echo "== map rotation: $r"
  grep -ho "\[SMTEST\] [PF][AI][SL].*" "$logs/vote.log" 2>/dev/null | sed 's/^\[SMTEST\] /  /' | awk '!seen[$0]++'
  exit $status
fi

echo "Building prefabs and test map"
copy_assets
run build -quit -executeMethod SourceMovementSetup.BuildTestScene || true
grep -q "Test scene saved" "$logs/build.log" || { echo "BUILD FAILED (see $logs/build.log)"; exit 1; }
# That checks the menu code. Then play-test the committed prefabs and map (what users get). KEEP_BUILD=1 copies
# the rebuilt ones into the repo instead: a rebuild gives every object new IDs, so only do it when the setup changed.
if [ -n "${KEEP_BUILD:-}" ]; then
  for f in SourceMovement/SourceMovement.prefab SourceMovement/SourceTestMap.unity SourceTimer/SourceTimer.prefab; do
    cp "$proj/Assets/$f" "$repo/Assets/$f"
  done
  # Plus files Unity made for new scripts (.meta, U# program assets), which the rebuilt map refers to.
  (cd "$proj/Assets" && find SourceMovement SourceTimer -type f) | while read -r f; do
    [ -e "$repo/Assets/$f" ] || { cp "$proj/Assets/$f" "$repo/Assets/$f"; echo "  new: Assets/$f"; }
  done
fi
copy_assets
status=0
for fps in "${@:-30 90 144}"; do
  for f in $fps; do
    if run "play_$f" -executeMethod PlayTestBootstrap.Run -smFrameRate "$f"; then r="ALL PASSED"; else r="FAILED"; status=1; fi
    echo "== $f fps: $r"
    grep -o "\[SMTEST\] .*" "$logs/play_$f.log" | grep -vE "PASS|^\[SMTEST\] $" | sed 's/^\[SMTEST\] /  /'
  done
done
exit $status
