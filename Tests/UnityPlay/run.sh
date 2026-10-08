#!/usr/bin/env bash
# Play-tests the prefab in a real Unity 2022.3.22f1 editor: builds the test map with the editor menu code,
# then runs PlayTestRunner in play mode with VRChat's ClientSim (real PhysX, ClientSim's player controller
# and input path) at each frame rate given (default 30 90 144).
# Needs an activated Unity license, UNITY pointing at the editor binary, and Tests/UdonCompile/setup.sh run
# once (it downloads the VRChat SDK). The Unity project is created in Tests/UnityPlay/.cache (git-ignored).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
unity=${UNITY:-/opt/unity/Editor/Unity}
proj=$here/.cache/Project
logs=$here/.cache/logs
sdk=$repo/Tests/UdonCompile/.cache/sdk
[ -d "$sdk/worlds" ] || { echo "Run Tests/UdonCompile/setup.sh first (downloads the VRChat SDK)"; exit 1; }
mkdir -p "$logs"

run() { # log name, Unity args...
  local log=$logs/$1.log; shift
  local x=(); command -v xvfb-run >/dev/null && x=(xvfb-run -a)
  "${x[@]}" "$unity" -batchmode -projectPath "$proj" -logFile "$log" "$@" >/dev/null 2>&1 && return 0
  grep -E "error CS|Exception|\[SMTEST\] FAIL" "$log" | head -20; return 1
}

if [ ! -d "$proj/Assets" ]; then
  echo "Creating Unity project"
  mkdir -p "$proj/Assets" "$proj/Packages"
  cp -r "$sdk/base" "$proj/Packages/com.vrchat.base"
  cp -r "$sdk/worlds" "$proj/Packages/com.vrchat.worlds"
  ln -s "$repo/Assets/SourceMovement" "$proj/Assets/SourceMovement"
  ln -s "$repo/Assets/SourceTimer" "$proj/Assets/SourceTimer"
  ln -s "$here" "$proj/Assets/SourcePlayTests"
  modules=""
  for m in ai androidjni animation assetbundle audio cloth director imageconversion imgui jsonserialize particlesystem physics physics2d screencapture terrain terrainphysics tilemap ui uielements umbra unityanalytics unitywebrequest unitywebrequestassetbundle unitywebrequestaudio unitywebrequesttexture unitywebrequestwww vehicles video vr wind xr; do
    modules="$modules\"com.unity.modules.$m\": \"1.0.0\", "
  done
  # The VRChat SDK's Unity dependencies, plus the test framework.
  cat > "$proj/Packages/manifest.json" <<JSON
{ "dependencies": { $modules
  "com.unity.burst": "1.8.7", "com.unity.collections": "2.1.4", "com.unity.mathematics": "1.2.6",
  "com.unity.nuget.newtonsoft-json": "3.2.1", "com.unity.timeline": "1.7.6", "com.unity.xr.management": "4.3.3",
  "com.unity.xr.oculus": "4.0.0", "com.unity.postprocessing": "3.2.2", "com.unity.ugui": "1.0.0",
  "com.unity.cinemachine": "2.9.7", "com.unity.textmeshpro": "3.0.6", "com.unity.inputsystem": "1.2.0",
  "com.unity.ai.navigation": "1.1.5" } }
JSON
  run open -quit || true # first import creates ProjectSettings
  # ClientSim reads input through the Input System, the scripts use the old Input Manager: VRChat projects use both.
  sed -i 's/activeInputHandler: 0/activeInputHandler: 2/' "$proj/ProjectSettings/ProjectSettings.asset"
fi

echo "Building prefabs and test map"
run build -quit -executeMethod SourceMovementSetup.BuildTestScene
# That checks the menu code. Play-test the committed prefabs and map (what users get) unless KEEP_BUILD is set:
# a rebuild gives every object new IDs, so only commit one when the setup code changed.
generated=(Assets/SourceMovement/SourceMovement.prefab Assets/SourceMovement/SourceTestMap.unity Assets/SourceTimer/SourceTimer.prefab)
[ -n "${KEEP_BUILD:-}" ] || git -C "$repo" checkout -- "${generated[@]}"
status=0
for fps in "${@:-30 90 144}"; do
  for f in $fps; do
    if run "play_$f" -executeMethod PlayTestBootstrap.Run -smFrameRate "$f"; then r="ALL PASSED"; else r="FAILED"; status=1; fi
    echo "== $f fps: $r"
    grep -o "\[SMTEST\] .*" "$logs/play_$f.log" | grep -vE "PASS|^\[SMTEST\] $" | sed 's/^\[SMTEST\] /  /'
  done
done
exit $status
