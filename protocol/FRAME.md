# Overlay frame (shared memory)

Outcast draws its own objects (hands, saber, effects, HUD) on a transparent background and writes each finished
frame to the file `/dev/shm/valoutcast_frame`. Valheim reads it and draws it over its own picture.

This is a separate channel from the UDP protocol in `PROTOCOL.md`, because it is large and only needs the newest frame.

| Offset | Type | Field |
|---|---|---|
| 0 | u32 | magic = 0x52464F56 (bytes `56 4F 46 52`, "VOFR") |
| 4 | u32 | version = 1 |
| 8 | u32 | width in pixels |
| 12 | u32 | height in pixels |
| 16 | u32 | frame counter: even = frame number x 2, complete. Odd = being written right now. |
| 20 | u32 | picture requests: the reading game adds one every frame it draws. The writing game draws one new picture per request, so the two games' frames stay in step. 0 means nobody is asking yet (the writer then draws freely). |
| 24 | f32 | wanted horizontal field of view in degrees, written by the reading game together with the picture requests. The writing game draws with it so its things line up with the reading game's world. 0 means not given (the writer uses its own). |
| 28 | 4 bytes | reserved |
| 32 | width x height x 4 bytes | pixels, RGBA, 8 bits each, the first row is the **bottom** of the picture (OpenGL order) |

Reading: read the counter, copy the pixels, read the counter again. Use the copy only if both reads were the same even number.

Pixels use ordinary (straight) alpha: red, green and blue are the color, alpha is how much it covers. Outcast converts from its own premultiplied drawing before publishing. Light with no coverage (additive glow) comes through as a see-through colored pixel.

Frames are published at most 45 times a second by default (`VO_OVERLAY_FPS` changes it), because copying a frame out of the graphics card is the slow part. Valheim only needs to draw the picture again when the counter changes.

The size is whatever size the Outcast window is. Valheim stretches it to its own screen, so start Outcast with the same shape as the Valheim window (see `tools/run_outcast.sh`). The Outcast window itself stays hidden.
