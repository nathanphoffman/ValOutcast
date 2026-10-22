#!/bin/bash
# Stops the processes that tools/start_test_session.sh started (and only those: it is safe while the real games run),
# and removes the frame file they left behind.
LOGS=/tmp/valoutcast_test
if [ -f "$LOGS/pids" ]; then
  for p in $(cat "$LOGS/pids"); do kill "$p" 2>/dev/null; done
  : > "$LOGS/pids"
fi
sleep 1
rm -f /dev/shm/valoutcast_frame
