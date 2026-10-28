#!/usr/bin/env python3
"""Saves the newest overlay frame (see protocol/FRAME.md) as a PNG, shown over a checkerboard so transparency is visible.

Usage: dump_frame.py OUT.png [--raw]     (--raw writes the pixels as they are, with alpha)
"""

import struct
import sys
import zlib

PATH = "/dev/shm/valoutcast_frame"


# The writer updates the file in place, so a read can catch it half-written; retry until a whole frame is read.
def read_frame():
    for _ in range(50):
        with open(PATH, "rb") as f:
            head = f.read(32)
            magic, version, w, h, counter = struct.unpack_from("<IIIII", head)

            if magic != 0x52464F56 or version != 1:
                raise SystemExit("not an overlay frame file")

            data = f.read(w * h * 4)
            f.seek(16)
            again = struct.unpack("<I", f.read(4))[0]

        if counter == again and counter % 2 == 0 and len(data) == w * h * 4:
            return w, h, counter // 2, data

    raise SystemExit("could not get a stable frame")


def write_png(path, w, h, rgba_rows, alpha):
    raw = b"".join(b"\x00" + row for row in rgba_rows)

    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body))

    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6 if alpha else 2, 0, 0, 0)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "frame.png"
    raw_mode = "--raw" in sys.argv
    w, h, number, data = read_frame()
    rows = []
    covered = 0

    for y in range(h - 1, -1, -1):  # the file's first row is the bottom
        row = data[y * w * 4:(y + 1) * w * 4]

        if raw_mode:
            rows.append(bytes(row))
        else:
            px = bytearray(w * 3)

            for x in range(w):
                r, g, b, a = row[4 * x:4 * x + 4]
                back = 90 if ((x // 16 + (h - 1 - y) // 16) % 2) else 150  # checkerboard
                inv = 255 - a
                px[3 * x] = (r * a + back * inv) // 255      # straight alpha: color * alpha + background * (1 - alpha)
                px[3 * x + 1] = (g * a + back * inv) // 255
                px[3 * x + 2] = (b * a + back * inv) // 255

            rows.append(bytes(px))

        covered += sum(1 for x in range(w) if row[4 * x + 3] > 8 or row[4 * x] + row[4 * x + 1] + row[4 * x + 2] > 24)

    write_png(out, w, h, rows, raw_mode)
    print("frame %d, %dx%d, %.1f%% of pixels have something in them" % (number, w, h, 100.0 * covered / (w * h)))


main()
