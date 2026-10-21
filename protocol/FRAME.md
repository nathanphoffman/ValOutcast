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
| 20 | 12 bytes | reserved |
| 32 | width x height x 4 bytes | pixels, RGBA, 8 bits each, the first row is the **bottom** of the picture (OpenGL order) |

Reading: read the counter, copy the pixels, read the counter again. Use the copy only if both reads were the same even number.

Pixels are close to *premultiplied* alpha: red, green and blue are already scaled by how much the pixel is covered. Where Outcast drew additive glow, the pixel has color but little or no alpha, which adds light when drawn over Valheim with the blend `result = pixel.rgb + background * (1 - pixel.a)`.

The size is whatever size the Outcast window is. Valheim stretches it to its own screen, so start Outcast with the same shape as the Valheim window (see `tools/run_outcast.sh`).
