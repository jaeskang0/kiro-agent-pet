"""Generate the original Spark sprite sheet using only Python's standard library."""

import math
from pathlib import Path
import struct
import zlib

WIDTH, HEIGHT = 1536, 1872
pixels = bytearray(WIDTH * HEIGHT * 4)


def ellipse(cx, cy, rx, ry, color):
    for y in range(max(0, int(cy - ry)), min(HEIGHT, int(cy + ry + 1))):
        for x in range(max(0, int(cx - rx)), min(WIDTH, int(cx + rx + 1))):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                index = (y * WIDTH + x) * 4
                pixels[index:index + 4] = bytes(color)


for row in range(9):
    for column in range(8):
        phase = column / 8 * math.tau
        cx = column * 192 + 96
        base = row * 208
        bob = math.sin(phase) * (8 if row in (4, 7) else 3)
        cy = base + 128 - abs(bob)
        ellipse(cx, base + 179, 38, 6, (64, 67, 91, 40))
        ellipse(cx, cy, 39, 46, (52, 57, 81, 255))
        ellipse(cx, cy - 3, 35, 41, (174, 197, 255, 255))
        ellipse(cx - 9, cy - 26, 14, 7, (215, 226, 255, 255))
        for side in (-1, 1):
            eye_height = 2 if row == 5 and column >= 4 else 5
            ellipse(cx + side * 12, cy - 2, 3, eye_height, (52, 57, 81, 255))
            ellipse(cx + side * 21, cy + 7, 5, 3, (250, 169, 184, 255))
        ellipse(cx, cy + 11, 4, 2, (52, 57, 81, 255))
        hand_y = cy + (math.sin(phase) * 12 if row == 3 else 10)
        ellipse(cx - 38, hand_y, 7, 10, (174, 197, 255, 255))
        ellipse(cx + 38, cy + 10, 7, 10, (174, 197, 255, 255))
        if row in (6, 8):
            ellipse(cx, cy + 26, 20, 8, (255, 205, 111, 255))


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


raw = b"".join(b"\0" + pixels[y * WIDTH * 4:(y + 1) * WIDTH * 4] for y in range(HEIGHT))
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 6, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
destination = Path(__file__).resolve().parents[1] / "src" / "KiroPet.Desktop" / "Assets" / "spark.png"
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_bytes(png)
print(f"Generated original Spark artwork: {destination}")
