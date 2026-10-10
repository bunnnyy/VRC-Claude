#!/usr/bin/env bash
# Downloads the test maps from GameBanana into Tests/Bsp/.cache/maps (git-ignored; maps belong to their authors).
# GameBanana sometimes blocks plain clients, so a browser User-Agent is sent and failed downloads are retried.
# Archives are zip, rar or 7z: needs 7-Zip (7z) and, for rar, unar (Debian/Ubuntu packages "7zip" and "unar").
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p .cache/maps .cache/archives
UA="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36"

# map name, GameBanana file id (gamebanana.com/dl/<id>), archive name; mod page and author in the comment
maps=(
  "bhop_japan 283793 bhop_japan.zip"            # gamebanana.com/mods/125304, tmontana (Tony Montana)
  "bhop_kitsune 279756 kitsune_f.zip"           # gamebanana.com/mods/126424, Ghost1447951
  "bhop_eazy_v2 304626 bhop_eazy_v2.rar"        # gamebanana.com/mods/124915, 31K4L
  "bhop_arcane_v1 319953 bhop_arcane_v1_3.7z"   # gamebanana.com/mods/124461, Panzerhandschuh
  "bhop_badges 318318 bhop_badges.rar"          # gamebanana.com/mods/124524, Badgeslol
)

for entry in "${maps[@]}"; do
  read -r name id archive <<<"$entry"
  [ -f ".cache/maps/$name.bsp" ] && { echo "$name: cached"; continue; }
  for attempt in 1 2 3 4; do
    curl -fsSL -A "$UA" -o ".cache/archives/$archive" "https://gamebanana.com/dl/$id" && break
    echo "$name: download failed (attempt $attempt), retrying"; sleep $((attempt * 4))
  done
  # Each archive in its own folder; take the .bsp inside (the largest one if there are several).
  out=".cache/archives/${archive%.*}"
  rm -rf "$out"; mkdir -p "$out"
  if [[ "$archive" == *.rar ]]; then unar -q -f -o "$out" ".cache/archives/$archive" >/dev/null
  else 7z x -y -o"$out" ".cache/archives/$archive" >/dev/null; fi
  bsp=$(find "$out" -iname "*.bsp" -printf "%s %p\n" | sort -rn | head -1 | cut -d' ' -f2-)
  [ -n "$bsp" ] || { echo "$name: no .bsp in $archive"; exit 1; }
  cp "$bsp" ".cache/maps/$name.bsp"
  echo "$name: $(du -h ".cache/maps/$name.bsp" | cut -f1) (from $(basename "$bsp"))"
done
