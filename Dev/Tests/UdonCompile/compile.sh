#!/usr/bin/env bash
# Compiles the UdonSharp scripts in Assets/ with VRChat's real UdonSharp compiler, outside Unity.
# Usage: Dev/Tests/UdonCompile/compile.sh [script.cs ...]   (default: every Assets/**/Scripts/*.cs)
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
cache=$here/.cache
bin=$cache/bin/Harness/Debug/net8.0
dotnet=$(command -v dotnet || echo /opt/dotnet/dotnet)

[ -f "$bin/UdonSharpHeadless.dll" ] || "$here/setup.sh"

if [ $# -gt 0 ]; then
  scripts=("$@")
else
  mapfile -t scripts < <(find "$repo/Assets" -path '*/Scripts/*.cs' | sort)
fi

# Editor scripts: compile everything in Assets/ like Unity's editor assembly would.
echo "Compiling Assets/ (including Editor scripts) against the Unity editor assemblies"
if ! DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$dotnet" build "$here/build/EditorCheck/EditorCheck.csproj" -v q -nologo > "$cache/editorcheck.log" 2>&1; then
  grep -E "error" "$cache/editorcheck.log" | sed 's/ \[.*//' | sort -u
  echo "EDITOR SCRIPT COMPILE FAILED"
  exit 1
fi
echo "Editor scripts OK"

# Rebuild the harness too (incremental), so changes to the shared tests are picked up.
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$dotnet" build "$here/build/Harness/Harness.csproj" -v q -nologo > "$cache/harness.log" 2>&1 \
  || { grep -E "error" "$cache/harness.log" | sed 's/ \[.*//' | sort -u; echo "HARNESS BUILD FAILED"; exit 1; }

# UdonSharp loads its messages from Packages/com.vrchat.worlds relative to the working directory.
mkdir -p "$cache/run/Packages"
ln -sfn "$cache/sdk/worlds" "$cache/run/Packages/com.vrchat.worlds"
cd "$cache/run"
"$dotnet" "$bin/UdonSharpHeadless.dll" "$cache/sdk" "$cache/unity/Editor/Data" "$bin" "$cache/uasm" ${UDON_TEST:+--test} "${scripts[@]}"
