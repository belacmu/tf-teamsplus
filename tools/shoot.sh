#!/bin/bash
# Screenshot the running game window (dev tool).
#
#   tools/shoot.sh out.png
#
# `screencapture -R` grabs a screen REGION, so the game has to be frontmost for
# the moment of the capture; the previously active app is put back afterwards.
set -e
OUT="${1:?usage: shoot.sh <out.png>}"

PREV=$(osascript -e 'tell application "System Events" to get name of first process whose frontmost is true' 2>/dev/null || echo "")
osascript -e 'tell application "System Events" to set frontmost of process "FortRise" to true' 2>/dev/null || true

BOUNDS=$(osascript -e 'tell application "System Events" to tell process "FortRise" to get {position, size} of window 1' 2>/dev/null)
X=$(echo "$BOUNDS" | cut -d, -f1 | tr -d ' ')
Y=$(echo "$BOUNDS" | cut -d, -f2 | tr -d ' ')
W=$(echo "$BOUNDS" | cut -d, -f3 | tr -d ' ')
H=$(echo "$BOUNDS" | cut -d, -f4 | tr -d ' ')

screencapture -x -R"$X","$Y","$W","$H" "$OUT"

if [ -n "$PREV" ]; then
  osascript -e "tell application \"System Events\" to set frontmost of process \"$PREV\" to true" 2>/dev/null || true
fi
echo "shot: $OUT (${W}x${H} at ${X},${Y})"
