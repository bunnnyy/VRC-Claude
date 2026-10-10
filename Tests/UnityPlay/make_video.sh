#!/usr/bin/env bash
# Turns a RouteRunner recording (run.sh route ... outdir) into an mp4: 50 fps frames, with the speed (bottom) and
# the map timer's text (top) burnt in from outdir/hud.txt, plus an optional title line for the first seconds.
#   make_video.sh outdir out.mp4 ["title"]
set -euo pipefail
dir=$1; out=$2; title=${3:-}
ass=$dir/hud.ass
python3 - "$dir/hud.txt" "$ass" "$title" <<'PY'
import sys
hud, ass, title = sys.argv[1], sys.argv[2], sys.argv[3]
def ts(frame):  # 50 fps frame -> h:mm:ss.cc
    cs = frame * 2
    return f"{cs // 360000}:{cs // 6000 % 60:02d}:{cs // 100 % 60:02d}.{cs % 100:02d}"
lines = [l.rstrip("\n").split("\t") for l in open(hud)]
out = ["[Script Info]", "ScriptType: v4.00+", "PlayResX: 1280", "PlayResY: 720", "",
       "[V4+ Styles]",
       "Format: Name, Fontname, Fontsize, PrimaryColour, OutlineColour, BackColour, Bold, BorderStyle, Outline, Shadow, Alignment, MarginV",
       "Style: Speed,DejaVu Sans,34,&H00FFFFFF,&H00000000,&H80000000,1,1,2,0,2,40",
       "Style: Timer,DejaVu Sans,30,&H00FFFFFF,&H00000000,&H80000000,1,1,2,0,8,24",
       "Style: Title,DejaVu Sans,26,&H00FFFFFF,&H00000000,&H80000000,0,1,2,0,1,24", "",
       "[Events]", "Format: Layer, Start, End, Style, Text"]
def runs(column):  # merge equal consecutive values into one event
    start = 0
    for i in range(1, len(lines) + 1):
        if i == len(lines) or lines[i][column] != lines[start][column]:
            if lines[start][column]:
                yield start, i, lines[start][column]
            start = i
for a, b, text in runs(0):
    out.append(f"Dialogue: 0,{ts(a)},{ts(b)},Speed,{text} u/s")
for a, b, text in runs(1):
    out.append(f"Dialogue: 0,{ts(a)},{ts(b)},Timer,{text}")
if title:
    out.append(f"Dialogue: 0,{ts(0)},{ts(250)},Title,{title}")
open(ass, "w").write("\n".join(out) + "\n")
PY
ffmpeg -y -loglevel error -framerate 50 -i "$dir/f%05d.jpg" -vf "ass=$ass" -c:v libx264 -preset slow -crf 26 \
  -pix_fmt yuv420p -movflags +faststart "$out"
echo "$out: $(du -h "$out" | cut -f1), $(ls "$dir"/f*.jpg | wc -l) frames"
