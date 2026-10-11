#!/usr/bin/env bash
# Collision test (round 30c) in Tom's test world (forest32 from Content/benchmarks.json): standing still, jumping,
# walking/sprinting into a trunk and a boulder, a slope steeper than 50°, landing on a cliff step.
# Runs before every build. Usage: tools/collision_test.sh [godot binary]   (exit 0 = all sites pass)
set -u
cd "$(dirname "$0")/.."
GODOT=${1:-godot}
world=$(python3 -I -c '
import json
w = json.load(open("Content/benchmarks.json"))["world"]
args = ["--preset=" + w["preset"], "--seed=" + str(w["seed"])] + ["--set=gen." + k + "=" + str(v) for k, v in w["gen"].items()]
print(" ".join(args))')
[ -n "$world" ] || { echo "could not read the test world from Content/benchmarks.json"; exit 2; }
# Sites: the world start (cliff step, steep slope), Tom's forest B, the lake overlook.
sites=("" "--at=-5286.068,4604.638" "--at=-5775.082,5791.284")
fail=0
for site in "${sites[@]}"; do
    log=$(mktemp)
    timeout 900 "$GODOT" --headless --path . res://Game/Rewrite/WorldLab.tscn -- $world --set=sim.weather=0 $site --collision-test > "$log" 2>&1
    code=$?
    echo "== ${site:-world start} (exit $code)"
    grep '^COLLISION_TEST' "$log" | grep -v 'rock hull kind .*PASS'
    [ $code -eq 0 ] || fail=1
    rm -f "$log"
done
[ $fail -eq 0 ] && echo "COLLISION TESTS PASSED" || echo "COLLISION TESTS FAILED"
exit $fail
