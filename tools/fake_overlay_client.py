#!/usr/bin/env python3
"""Acts like Valheim's overlay: asks for one picture per 'frame', waits for it, and reports how long Outcast took.
Also counts how many pictures contain flash/bolt-colored pixels. Use it against a running Outcast (tools/start_test_session.sh).
usage: fake_overlay_client.py SECONDS [FRAME_MS]        (FRAME_MS = pretend Valheim frame time, default 20)"""
import mmap, os, struct, sys, time

PATH = "/dev/shm/valoutcast_frame"
secs = float(sys.argv[1])
frame_ms = float(sys.argv[2]) if len(sys.argv) > 2 else 20.0
while not os.path.exists(PATH):
    time.sleep(0.1)
fd = os.open(PATH, os.O_RDWR)
m = mmap.mmap(fd, os.fstat(fd).st_size)
w, h = struct.unpack_from("<II", m, 8)
req = struct.unpack_from("<I", m, 20)[0]
last = struct.unpack_from("<I", m, 16)[0]
lat, timeouts, with_red, pics = [], 0, 0, 0
t_end = time.time() + secs
while time.time() < t_end:
    t0 = time.time()
    req = (req + 1) & 0xFFFFFFFF or 1
    struct.pack_into("<I", m, 20, req)       # the request counter (see protocol/FRAME.md)
    struct.pack_into("<f", m, 24, 97.0)      # Valheim's horizontal field of view
    got = False
    while time.time() - t0 < 0.025:
        c = struct.unpack_from("<I", m, 16)[0]
        if c != last and c % 2 == 0:
            got, last = True, c
            break
        time.sleep(0.0002)
    if got:
        lat.append((time.time() - t0) * 1000)
        pics += 1
        px = m[32:32 + w * h * 4]
        red = 0
        for y in range(h // 4, 3 * h // 4, 4):
            row = px[(y * w) * 4:(y * w + w) * 4]
            for x in range(w // 4, 3 * w // 4, 4):
                r, g, b, a = row[x * 4:x * 4 + 4]
                if a > 60 and r > 200 and g < 120 and b < 120:
                    red += 1
        with_red += 1 if red else 0
    else:
        timeouts += 1
    rest = frame_ms / 1000 - (time.time() - t0)
    if rest > 0:
        time.sleep(rest)
lat.sort()
n = len(lat)
print("requests %d, pictures %d, timeouts(25ms) %d, pictures with red pixels %d" % (pics + timeouts, pics, timeouts, with_red))
if n:
    print("latency ms: avg %.1f  median %.1f  p95 %.1f  max %.1f" % (sum(lat) / n, lat[n // 2], lat[int(n * 0.95)], lat[-1]))
