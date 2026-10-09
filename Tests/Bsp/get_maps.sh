#!/usr/bin/env bash
# Downloads the test maps from GameBanana into Tests/Bsp/.cache/maps (git-ignored; maps belong to their authors).
# GameBanana sometimes blocks plain clients, so a browser User-Agent is sent and failed downloads are retried.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .cache/maps .cache/zips
UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36"

# name  GameBanana file id (gamebanana.com/dl/<id>)  mod page
maps=(
  "bhop_japan 283793"   # gamebanana.com/mods/125304
)

for entry in "${maps[@]}"; do
  read -r name id <<<"$entry"
  [ -f ".cache/maps/$name.bsp" ] && { echo "$name: cached"; continue; }
  for attempt in 1 2 3 4; do
    curl -fsSL -A "$UA" -o ".cache/zips/$name.zip" "https://gamebanana.com/dl/$id" && break
    echo "$name: download failed (attempt $attempt), retrying"; sleep $((attempt * 4))
  done
  unzip -o -j -q ".cache/zips/$name.zip" '*.bsp' -d .cache/maps
  echo "$name: $(du -h ".cache/maps/$name.bsp" | cut -f1)"
done
