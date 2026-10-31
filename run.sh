#!/bin/bash
# Launch the modded game — one instance only.
#
#   ./run.sh                 play
#   ./run.sh --selftest      scripted 4-player run (screenshots into ./shots)
#   ./run.sh --selftest 8    scripted 8-player run (needs WiderSet installed)
#   ./run.sh --revive        scripted Team Revive run (2v1 + wolf, variant on);
#                            grep the log for "SELFTEST REVIVE"
# Scripted runs are silent and keep running when the window is not in front.
#
# Two instances must never run at once: the second cannot write
# Mods/_RelinkerCache/ThreeTeamMode.ThreeTeamMode.dll and silently runs stale
# code, so this script closes any existing one first. Note `pkill -f FortRise`
# does NOT match it — the process command line is just "./FortRise" — hence
# `pkill -x`.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
FORTRISE="${FORTRISE_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/TowerFall/TowerFall.app/Contents/FortRise.app/Contents/MacOS}"

if [ ! -x "$FORTRISE/FortRise" ]; then
  echo "FortRise not found at: $FORTRISE" >&2
  echo "set FORTRISE_DIR to its Contents/MacOS folder" >&2
  exit 1
fi

if pgrep -x FortRise >/dev/null; then
  echo "closing the running instance..."
  pkill -x FortRise
  while pgrep -x FortRise >/dev/null; do sleep 1; done
  sleep 1
fi

if ! pgrep -x steam_osx >/dev/null; then
  echo "note: Steam is not running — FortRise will bounce the launch through" \
       "steam://run and exit. Start Steam first."
fi

if [ "$1" = "--selftest" ] || [ "$1" = "--revive" ]; then
  export THREETEAM_SELFTEST=1
  export THREETEAM_SHOTDIR="${THREETEAM_SHOTDIR:-$HERE/shots}"
  [ -n "$2" ] && export THREETEAM_SELFTEST_PLAYERS="$2"
  if [ "$1" = "--revive" ]; then
    export THREETEAM_SELFTEST_REVIVE=1
    echo "team-revive run; watch the log for SELFTEST REVIVE PASS/FAIL"
  fi
  echo "self-test on; screenshots -> $THREETEAM_SHOTDIR"
fi

cd "$FORTRISE"
if [ -n "$THREETEAM_SELFTEST" ]; then
  # A scripted run keeps playing behind other windows (the game pauses itself when
  # it loses focus) and is silent (the self-test mutes it; THREETEAM_SELFTEST_SOUND=1
  # keeps the sound).
  exec ./FortRise --no-auto-pause
fi
exec ./FortRise
