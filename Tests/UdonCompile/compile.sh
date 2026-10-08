#!/usr/bin/env bash
# Compiles the UdonSharp scripts in Assets/ with VRChat's real UdonSharp compiler, outside Unity.
# Usage: Tests/UdonCompile/compile.sh [script.cs ...]   (default: every Assets/**/Scripts/*.cs)
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
cache=$here/.cache
bin=$cache/bin/Harness/Debug/net8.0
dotnet=$(command -v dotnet || echo /opt/dotnet/dotnet)

[ -f "$bin/UdonSharpHeadless.dll" ] || "$here/setup.sh"

if [ $# -gt 0 ]; then
  scripts=("$@")
else
  mapfile -t scripts < <(find "$repo/Assets" -path '*/Scripts/*.cs' | sort)
fi

# UdonSharp loads its messages from Packages/com.vrchat.worlds relative to the working directory.
mkdir -p "$cache/run/Packages"
ln -sfn "$cache/sdk/worlds" "$cache/run/Packages/com.vrchat.worlds"
cd "$cache/run"
"$dotnet" "$bin/UdonSharpHeadless.dll" "$cache/sdk" "$cache/unity/Editor/Data" "$bin" "$cache/uasm" ${UDON_TEST:+--test} "${scripts[@]}"
