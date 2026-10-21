#!/bin/bash
# Starts Outcast in overlay mode (it only draws its own things, and publishes frames for Valheim).
# Usage: tools/run_outcast.sh [map] [width height]     defaults: artus_mine 1280 720
# Use the same shape as your Valheim window, because Valheim stretches the overlay to fit.
# VO_OVERLAY_FPS sets how many times a second Outcast's picture is handed to Valheim (default 45).
# VO_SV_FPS sets how many times a second the game logic runs (Outcast normally does 20, which feels laggy here).
# Console messages are not drawn on the picture (con_notifytime 0).
# Outcast draws 45 pictures a second. Fast things like bullets show up better at higher rates, but it takes more of the graphics card from Valheim.
MAP="${1:-artus_mine}"
W="${2:-1280}"
H="${3:-720}"
cd "$(dirname "$0")/../third_party/jk2run" || exit 1
# VO_GIVE_ALL=1 gives every weapon (lightsaber included) and full ammo each time you start a level, for testing. Set VO_GIVE_ALL= (empty) to turn it off.
VO_GIVE_ALL="${VO_GIVE_ALL-1}" VO_OVERLAY=1 exec ./openjo_sp.x86_64 +set fs_basepath "$PWD" +set r_mode -1 +set r_customwidth "$W" +set r_customheight "$H" +set r_fullscreen 0 +set sv_fps "${VO_SV_FPS:-60}" +set com_maxfps 45 +set com_maxfpsUnfocused 45 +set con_notifytime 0 +set s_musicvolume 0 +devmap "$MAP"
