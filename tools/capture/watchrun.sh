#!/bin/sh
# watchrun.sh <log> <command...>: run a game command; if its log does not grow for 120 s, kill it (HUNG).
LOG=$1; shift
START=$(powershell -NoProfile -Command "(Get-Date).ToString('yyyy-MM-ddTHH:mm:ss')")
"$@" > "$LOG" 2>&1 &
PID=$!
last=0; still=0
while kill -0 $PID 2>/dev/null; do
  sleep 5
  size=$(stat -c %s "$LOG" 2>/dev/null || echo 0)
  if [ "$size" = "$last" ]; then still=$((still+5)); else still=0; last=$size; fi
  if [ $still -ge 120 ]; then
    echo "HUNG at $(date +%T): log unchanged for 120 s; last line: $(tail -1 "$LOG" | cut -c1-160)"
    powershell -NoProfile -Command "Get-Process | Where-Object { (\$_.ProcessName -like 'Godot*' -or \$_.ProcessName -like 'Thornwake*') -and \$_.StartTime -ge [datetime]'$START' } | Stop-Process -Force" 2>/dev/null
    exit 2
  fi
done
wait $PID; echo "finished, exit $?"
