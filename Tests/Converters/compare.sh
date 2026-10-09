#!/usr/bin/env bash
# Compares the two BSP converters on a map in real Unity (step 2 of docs/MAPVOTE_PLAN.md):
#   DeadZoneLuna/uSource and Shane-SDK/USource, each in its own copy of the UnityPlay test project.
# Usage: Tests/Converters/compare.sh path/to/map.bsp   (needs Tests/UnityPlay/run.sh to have created the project)
# The converters are downloaded into .cache (git-ignored, neither states a license) at the commits below.
# Stock CS:S textures aren't available here (no game install), so only textures packed in the BSP can show.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
unity=${UNITY:-/opt/unity/Editor/Unity}
base=$repo/Tests/UnityPlay/.cache/Project
cache=$here/.cache
bsp=$(realpath "$1")
name=$(basename "$bsp" .bsp)
[ -d "$base/Library" ] || { echo "Run Tests/UnityPlay/run.sh first (creates the Unity project)"; exit 1; }
mkdir -p "$cache/src" "$cache/logs" "$cache/shots"

fetch() { # dir url commit
  [ -d "$cache/src/$1" ] || git clone -q "$2" "$cache/src/$1"
  git -C "$cache/src/$1" checkout -q "$3"
}
fetch uSource https://github.com/DeadZoneLuna/uSource 01ab6a2f080ce7341b87f2d867bfae079c0fa06e
fetch USource https://github.com/Shane-SDK/USource 46cfbe49f61d12d7a4b22fcc6e1a7788d9c5bf18
# USource keeps its DLLs, icons and fonts in Git LFS: fetch the real files instead of the pointers.
(cd "$cache/src/USource" && grep -rl "^version https://git-lfs" Assets | while read -r f; do
  curl -fsSL -o "$f" "https://media.githubusercontent.com/media/Shane-SDK/USource/46cfbe49f61d12d7a4b22fcc6e1a7788d9c5bf18/$f"
done) || true

# uSource loads maps from a game folder: fake one holding just the map.
mkdir -p "$cache/game/cstrike/maps"
ln -sf "$bsp" "$cache/game/cstrike/maps/$name.bsp"

project() { # name, converter folder to copy into Assets
  local p=$cache/$1
  rm -rf "$p"; mkdir -p "$p"
  cp -r "$base/Assets" "$base/Packages" "$base/ProjectSettings" "$base/Library" "$p/"
  rm -rf "$p/Assets/SourcePlayTests" "$p/Assets/MapTest.unity" "$p/Assets/SourceMapsImported"
  rm -rf "$p/Assets/SourceMaps"; cp -r "$repo/Assets/SourceMaps" "$p/Assets/"
  mkdir -p "$p/Assets/ConverterTest/Editor"
  cp "$here/Editor/ConverterMeasure.cs" "$here/Editor/$2" "$p/Assets/ConverterTest/Editor/"
}

run() { # project, log, method
  local x=(); command -v xvfb-run >/dev/null && x=(xvfb-run -a)
  "${x[@]}" "$unity" -batchmode -projectPath "$cache/$1" -logFile "$cache/logs/$2.log" -executeMethod "$3" \
    -bsp "$bsp" -gameRoot "$cache/game" -quit >/dev/null 2>&1 || echo "  (Unity exited with an error, see $cache/logs/$2.log)"
  grep -h "\[CONV\]" "$cache/logs/$2.log" | sort -u | sed 's/^\[CONV\] /  /'
  echo "  errors in log: $(grep -cE "^(NullReference|Exception|.*Exception:|.*error CS)" "$cache/logs/$2.log" || true)"
}

if [ "${ONLY:-}" != "USource" ]; then
echo "== DeadZoneLuna/uSource"
project uSource ImportWithDeadZoneLuna.cs
cp -r "$cache/src/uSource" "$cache/uSource/Assets/uSource"
rm -rf "$cache/uSource/Assets/uSource/.git"
# Its asmdef lacks "allow unsafe code", which its MDL reader needs.
sed -i 's|"references": \[\],|"references": [], "allowUnsafeCode": true,|' "$cache/uSource/Assets/uSource/uSource.asmdef"
run uSource "uSource_$name" ImportWithDeadZoneLuna.Run
fi

echo "== Shane-SDK/USource"
project USource ImportWithShane.cs
cp -r "$cache/src/USource/Assets/USource" "$cache/USource/Assets/USource"
# VRChat worlds use the built-in render pipeline; USource's materials use a URP shader graph. Test patch only:
sed -i -E 's#Shader.Find\("(Shader Graphs/[^"]*|Universal Render Pipeline/Lit)"\)#Shader.Find("Standard")#' \
  "$cache/USource/Assets/USource/Code/Converters/MaterialConverter.cs" "$cache/USource/Assets/USource/Code/Converters/BspConverter.cs"
# An unused "using PlasticPipe..." needs Unity's version control package, which VRChat projects don't have.
grep -rl "using PlasticPipe" "$cache/USource/Assets/USource" | xargs -r sed -i 's/^\(\xEF\xBB\xBF\)\?using PlasticPipe.*$/\1/' || true
# USource's static setup runs while a fresh project is still importing, fails, and stays failed for that editor
# session (a failed static constructor isn't retried). So open the project once to finish importing first.
x=(); command -v xvfb-run >/dev/null && x=(xvfb-run -a)
"${x[@]}" "$unity" -batchmode -projectPath "$cache/USource" -logFile "$cache/logs/USource_warmup.log" -quit >/dev/null 2>&1 || true
run USource "USource_$name" ImportWithShane.Run
