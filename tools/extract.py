#!/usr/bin/env python3
"""Extract named subtextures from a TowerFall atlas into standalone PNGs."""
import argparse, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "additional-characters", "smash-roster-mod"))
import build_unit as BU
import struct, zlib


def write_png(path, w, h, rgba):
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]
    ihdr = struct.pack(">IIBB", w, h, 8, 6) + b"\x00\x00\x00"
    data = (BU.PNG_SIG + BU.chunk(b"IHDR", ihdr)
            + BU.chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + BU.chunk(b"IEND", b""))
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    open(path, "wb").write(data)

ap = argparse.ArgumentParser()
ap.add_argument("--atlas", required=True)          # path without extension
ap.add_argument("--out", required=True)
ap.add_argument("names", nargs="+")
a = ap.parse_args()

xml = open(a.atlas + ".xml", encoding="utf-8-sig").read()
rects = {m.group(1): tuple(int(m.group(k)) for k in (2, 3, 4, 5))
         for m in re.finditer(r'<SubTexture name="([^"]+)" x="(\d+)" y="(\d+)" width="(\d+)" height="(\d+)"', xml)}
aw, ah, apx = BU.load_atlas_rgba(a.atlas + ".png")
for n in a.names:
    if n not in rects:
        print("missing:", n); continue
    sx, sy, sw, sh = rects[n]
    sub = bytearray(sw * sh * 4)
    BU.blit(sub, sw, apx, aw, sx, sy, sw, sh, 0, 0)
    p = os.path.join(a.out, n.replace("/", "_") + ".png")
    write_png(p, sw, sh, sub)
    print("wrote", p, f"{sw}x{sh}")
