#!/usr/bin/env bash
# Joins RouteRunner recordings into one video: stitch_video.sh out.mp4 list.txt, each line
# "recorddir[:first-last]|title|sound.wav:gain ..." -> a clip (make_video.sh, those shots, the sounds looped under it).
set -euo pipefail
out=$1; list=$2; tmp=$(mktemp -d); n=0
mv_sh=$(dirname "$0")/make_video.sh
: > $tmp/concat.txt
while IFS='|' read -r dir title sounds; do
  [ -z "$dir" ] && continue
  n=$((n+1)); clip=$tmp/clip$n.mp4; first=0; last=""
  if [[ "$dir" == *:* ]]; then range=${dir##*:}; dir=${dir%:*}; first=${range%-*}; last=${range#*-}; fi
  FIRST=$first LAST=$last SCALE=${SCALE:-960:540} CRF=${CRF:-28} $mv_sh "$dir" $tmp/v$n.mp4 "$title" > /dev/null < /dev/null
  inputs=(); filt=""; k=1; labels=""
  for s in $sounds; do
    inputs+=(-stream_loop -1 -i "${s%%:*}"); filt+="[$k:a]aresample=44100,aformat=channel_layouts=stereo,volume=${s##*:}[a$k];"; labels+="[a$k]"; k=$((k+1))
  done
  if [ $k -eq 1 ]; then inputs+=(-f lavfi -i anullsrc=r=44100:cl=stereo); filt="[1:a]anull[a]"; else filt+="${labels}amix=inputs=$((k-1)):normalize=0[a]"; fi
  ffmpeg -nostdin -y -loglevel error -i $tmp/v$n.mp4 "${inputs[@]}" -filter_complex "$filt" -map 0:v -map "[a]" -shortest -c:v copy -c:a aac -b:a 96k $clip
  echo "file '$clip'" >> $tmp/concat.txt
done < "$list"
ffmpeg -y -loglevel error -f concat -safe 0 -i $tmp/concat.txt -c copy -movflags +faststart "$out"
echo "$out: $(du -h "$out" | cut -f1), $n clips"
