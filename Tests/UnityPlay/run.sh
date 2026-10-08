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
  "${x[@]}" "$unity" -batchmode -projectPath "$proj" -logFile "$log" "$@" >/dev/null 2>&1 && return 0
  grep -E "error CS|Exception|\[SMTEST\] FAIL" "$log" | head -20; return 1
}

if [ ! -d "$proj/Assets" ]; then
  echo "Creating a VRChat world project with vpm (the Creator Companion CLI)"
  mkdir -p "$(dirname "$proj")"
  vpm install templates
  vpm new Project World -p "$(dirname "$proj")"
  vpm add package com.vrchat.worlds@3.10.5 -p "$proj"
  ln -s "$here" "$proj/Assets/SourcePlayTests"
fi

# The project gets a copy of the assets, so Unity never rewrites the committed files.
copy_assets() {
  for d in SourceMovement SourceTimer; do
    rm -rf "$proj/Assets/$d"
    cp -r "$repo/Assets/$d" "$proj/Assets/$d"
    cp "$repo/Assets/$d.meta" "$proj/Assets/"
  done
}

echo "Building prefabs and test map"
copy_assets
run build -quit -executeMethod SourceMovementSetup.BuildTestScene
# That checks the menu code. Then play-test the committed prefabs and map (what users get). KEEP_BUILD=1 copies
# the rebuilt ones into the repo instead: a rebuild gives every object new IDs, so only do it when the setup changed.
if [ -n "${KEEP_BUILD:-}" ]; then
  for f in SourceMovement/SourceMovement.prefab SourceMovement/SourceTestMap.unity SourceTimer/SourceTimer.prefab; do
    cp "$proj/Assets/$f" "$repo/Assets/$f"
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
