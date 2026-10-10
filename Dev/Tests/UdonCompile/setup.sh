#!/usr/bin/env bash
# Downloads what the headless UdonSharp compiler needs into .cache/ (git-ignored) and builds it:
# the VRChat Worlds SDK (which contains UdonSharp), Unity's managed DLLs and a few Unity packages.
# Set UNITY_TAR to a local Unity.tar.xz to skip the ~3.3 GB Unity download.
set -euo pipefail
cd "$(dirname "$0")"
cache=.cache
sdk_version=3.10.5
unity_hash=887be4894c44 # Unity 2022.3.22f1, the version VRChat uses
dotnet=$(command -v dotnet || echo /opt/dotnet/dotnet)
mkdir -p "$cache"

for pkg in worlds base; do
  if [ ! -d "$cache/sdk/$pkg" ]; then
    echo "Downloading VRChat $pkg SDK $sdk_version"
    curl -fsSL -o "$cache/$pkg.zip" "https://github.com/vrchat/packages/releases/download/$sdk_version/com.vrchat.$pkg-$sdk_version.zip"
    mkdir -p "$cache/sdk/$pkg"
    unzip -q "$cache/$pkg.zip" -d "$cache/sdk/$pkg"
    rm "$cache/$pkg.zip"
  fi
done

if [ ! -d "$cache/unity/Editor/Data/Managed" ]; then
  echo "Extracting Unity managed DLLs (takes a few minutes)"
  mkdir -p "$cache/unity"
  paths=('Editor/Data/Managed/*' 'Editor/Data/NetStandard/*' 'Editor/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui/*')
  if [ -n "${UNITY_TAR:-}" ]; then
    tar -xJf "$UNITY_TAR" -C "$cache/unity" --wildcards "${paths[@]}"
  else
    curl -fsSL "https://download.unity3d.com/download_unity/$unity_hash/LinuxEditorInstaller/Unity.tar.xz" | tar -xJ -C "$cache/unity" --wildcards "${paths[@]}"
  fi
fi

package() {
  if [ ! -d "$cache/packages/$1" ]; then
    echo "Downloading $1 $2"
    mkdir -p "$cache/packages/$1"
    curl -fsSL "https://download.packages.unity.com/$1/-/$1-$2.tgz" | tar -xz -C "$cache/packages/$1" 2>/dev/null
  fi
}
package com.unity.textmeshpro 3.0.6
package com.unity.cinemachine 2.9.7
package com.unity.ai.navigation 1.1.5
package com.unity.postprocessing 3.2.2
package com.unity.mathematics 1.2.6

python3 patch_udonsharp.py "$cache/sdk" "$cache/src/UdonSharpEditor"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$dotnet" build build/Harness/Harness.csproj -v q -nologo
echo "Setup done"
