#!/bin/bash
# Starts Outcast (silent: SDL's dummy audio driver) and the fake world, both in the background, logging to /tmp/valoutcast_test/.
# Usage: tools/start_test_session.sh [fake_world.py options]     e.g.  --fire --turn --nowall --noactors --weapon 3
# Env such as VO_DEBUG_BOLTS=1 or VO_BOLT_SPEED=1 passes through. Stop with tools/stop_test_session.sh.
# The real games must be closed: the ports 47100/47101 and /dev/shm/valoutcast_frame are shared with them.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LOGS=/tmp/valoutcast_test
mkdir -p "$LOGS"
: > "$LOGS/pids"
cd "$ROOT"
rm -f /dev/shm/valoutcast_frame
SDL_AUDIODRIVER=dummy tools/run_outcast.sh > "$LOGS/outcast.log" 2>&1 &
echo $! >> "$LOGS/pids"
sleep 1
python3 tools/fake_world.py "$@" > "$LOGS/fake.log" 2>&1 &
echo $! >> "$LOGS/pids"
echo "started; logs in $LOGS (outcast.log, fake.log). The level takes about 15-20 s to load and skip its intro."
