#!/usr/bin/env python3
"""Captures a short run of consecutive overlay pictures right after the gun fires (the muzzle flash appears) and stacks the
upper part of four of them into one PNG, so you can see what a shot looks like frame by frame.
Needs a running Outcast that is firing (tools/start_test_session.sh --fire --weapon 3 ...). Acts like Valheim: it requests each picture.
usage: frame_series.py OUT.png [STEP_SECONDS]        (default step 0.02 s between pictures)"""

import mmap, os, struct, sys, time, zlib

PATH = "/dev/shm/valoutcast_frame"
out_path = sys.argv[1]
step = float(sys.argv[2]) if len(sys.argv) > 2 else 0.02
fd = os.open(PATH, os.O_RDWR)
m = mmap.mmap(fd, os.fstat(fd).st_size)
w, h = struct.unpack_from("<II", m, 8)
req = struct.unpack_from("<I", m, 20)[0]
last = struct.unpack_from("<I", m, 16)[0]


# Request one frame from the world and wait for the reply.
def get():
    global req, last
    t0 = time.time()
    req = (req + 1) & 0xFFFFFFFF or 1
    struct.pack_into("<I", m, 20, req)
    struct.pack_into("<f", m, 24, 97.0)

    while time.time() - t0 < 0.05:
        c = struct.unpack_from("<I", m, 16)[0]

        if c != last and c % 2 == 0:
            last = c

            break

        time.sleep(0.0002)

    return bytes(m[32:32 + w * h * 4])


def flash(px):   # a big bright blob over the barrel = the muzzle flash
    n = 0

    for y in range(h - 520, h - 380, 3):
        row = px[(y * w) * 4:(y * w + w) * 4]

        for x in range(580, 820, 3):
            r, g, b, a = row[x * 4:x * 4 + 4]

            if a > 100 and r > 200 and g > 150:
                n += 1

    return n


frames, prev, t_end = [], 0, time.time() + 25

while time.time() < t_end and not frames:
    px = get()
    f = flash(px)

    if f > 40 and prev <= 40:
        frames = [px]

        for _ in range(7):
            time.sleep(step)
            frames.append(get())

    prev = f

if not frames:
    raise SystemExit("no muzzle flash seen: is Outcast firing?")

sel = [frames[i] for i in (0, 2, 4, 7)]
D = 2
cw, ch = w // D, (h * 3 // 4) // D
rows = []

for px in sel:
    for yy in range(ch):
        y = h - 1 - yy * D
        line = bytearray()

        for xx in range(cw):
            k = (y * w + xx * D) * 4
            r, g, b, a = px[k:k + 4]
            bg = 0x60 if ((xx // 8 + yy // 8) % 2) else 0x90
            al = a / 255
            line += bytes(int(c * al + bg * (1 - al)) for c in (r, g, b))

        rows.append(b"\x00" + bytes(line))

    rows.append(b"\x00" + b"\xff\x00\xff" * cw)   # a magenta line between pictures


def chunk(t, b):
    c = struct.pack(">I", len(b)) + t + b

    return c + struct.pack(">I", zlib.crc32(t + b) & 0xffffffff)


with open(out_path, "wb") as f:
    f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", cw, len(rows), 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b""))

print("wrote", out_path)
