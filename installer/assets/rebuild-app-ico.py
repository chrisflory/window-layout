"""Build a Windows-shell-friendly .ico using BMP/DIB frames (no PNG compression)."""
from __future__ import annotations

import struct
from pathlib import Path

from PIL import Image

SRC = Path(
    r"C:\Users\cflor\.cursor\projects\c-Users-cflor-Cursor\assets\window-layout-icon-options\SELECTED-e-wl-arrows-source.png"
)
OUT = Path(r"C:\Users\cflor\Cursor\scripts\window-layout-kit\installer\assets\app.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]


def image_to_dib(img: Image.Image) -> bytes:
    """32-bit BITMAPINFOHEADER + BGRA XOR bitmap + empty AND mask (ICO style)."""
    img = img.convert("RGBA")
    w, h = img.size
    # ICO DIB stores rows bottom-up; AND mask is 1bpp padded to 32-bit rows
    pixels = img.tobytes()
    bgra = bytearray()
    # convert RGBA top-down to BGRA bottom-up
    stride = w * 4
    for y in range(h - 1, -1, -1):
        row = pixels[y * stride : (y + 1) * stride]
        for i in range(0, len(row), 4):
            r, g, b, a = row[i : i + 4]
            bgra += bytes((b, g, r, a))

    # AND mask: 1 bit per pixel, rows padded to 4 bytes, also bottom-up, all zero (use alpha)
    and_row = ((w + 31) // 32) * 4
    and_mask = bytes(and_row * h)

    header = struct.pack(
        "<IIIHHIIIIII",
        40,  # biSize
        w,
        h * 2,  # height includes AND mask
        1,  # planes
        32,  # bit count
        0,  # compression BI_RGB
        len(bgra),
        0,
        0,
        0,
        0,
    )
    return header + bytes(bgra) + and_mask


def main() -> None:
    src = Image.open(SRC).convert("RGBA")
    w, h = src.size
    side = max(w, h)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(src, ((side - w) // 2, (side - h) // 2), src)

    frames: list[tuple[int, bytes]] = []
    for s in SIZES:
        dib = image_to_dib(canvas.resize((s, s), Image.Resampling.LANCZOS))
        frames.append((s, dib))

    # ICONDIR + ICONDIRENTRY table + image data
    count = len(frames)
    offset = 6 + 16 * count
    ico = bytearray(struct.pack("<HHH", 0, 1, count))
    blobs: list[bytes] = []
    for s, dib in frames:
        blobs.append(dib)
        ico += struct.pack(
            "<BBBBHHII",
            0 if s >= 256 else s,
            0 if s >= 256 else s,
            0,
            0,
            1,
            32,
            len(dib),
            offset,
        )
        offset += len(dib)
    for b in blobs:
        ico += b

    OUT.write_bytes(ico)
    print(f"wrote {OUT} ({len(ico)} bytes, {count} BMP frames)")


if __name__ == "__main__":
    main()
