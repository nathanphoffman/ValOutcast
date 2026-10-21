#!/bin/bash
# Starts Outcast in overlay mode (it only draws its own things, and publishes frames for Valheim).
# Usage: tools/run_outcast.sh [map] [width height]     defaults: artus_mine 1280 720
# Use the same shape as your Valheim window, because Valheim stretches the overlay to fit.
MAP="${1:-artus_mine}"
W="${2:-1280}"
H="${3:-720}"
cd "$(dirname "$0")/../third_party/jk2run" || exit 1
VO_OVERLAY=1 exec ./openjo_sp.x86_64 +set fs_basepath "$PWD" +set r_mode -1 +set r_customwidth "$W" +set r_customheight "$H" +set r_fullscreen 0 +devmap "$MAP"
