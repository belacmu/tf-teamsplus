#!/usr/bin/env python3
"""Generate every image asset the PC (FortRise 5) three-team mod ships.

Outputs, both under pc-fortrise/Content/:
  menu/teamC2x.png                   gold select-screen banner (recolored teamA2x)
  gameModes/threeteam.png            versus-carousel icon (TDM silhouette, 3 color bands)

Player sheets are NOT generated here: the mod recolours them at runtime from
whatever is in the atlas, which is the only way custom archers can get gold and
white versions. src/Recolor.cs is the source of truth for that hue math; the
banner below has its own, brighter mapping because it is a flat block of navy
rather than a shaded sprite.

Usage:
  python3 tools/gen_assets.py                     # macOS Steam default paths
  python3 tools/gen_assets.py --game <TowerFall dir>
"""
import argparse
import colorsys
import json
import os
import re
import struct
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(REPO, "additional-characters", "smash-roster-mod"))
import build_unit as BU  # noqa: E402  (pure-python PNG helpers: no Pillow on this box)

DEFAULT_GAME = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common/TowerFall")

ap = argparse.ArgumentParser()
ap.add_argument("--game", default=DEFAULT_GAME, help="TowerFall install root")
ap.add_argument("--out", default=os.path.join(HERE, "..", "Content"))
ARGS = ap.parse_args()

GAME = ARGS.game
OUT = os.path.abspath(ARGS.out)
RES = os.path.join(GAME, "TowerFall.app", "Contents", "Resources")
if not os.path.isdir(RES):                      # windows / linux layout
    RES = GAME
SPRITEDATA = os.path.join(RES, "Content", "Atlas", "SpriteData", "spriteData.xml")
MENU_ATLAS = os.path.join(RES, "Content", "Atlas", "menuAtlas")
# DarkWorld atlas is the superset TFGame.Atlas loads when the DLC is installed.
GAME_ATLAS = os.path.join(GAME, "DarkWorldContent", "Atlas", "atlas")
if not os.path.exists(GAME_ATLAS + ".xml"):
    GAME_ATLAS = os.path.join(RES, "Content", "Atlas", "atlas")


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


def read_atlas(base):
    xml = open(base + ".xml", encoding="utf-8-sig").read()
    rects = {m.group(1): tuple(int(m.group(k)) for k in (2, 3, 4, 5))
             for m in re.finditer(
                 r'<SubTexture name="([^"]+)" x="(\d+)" y="(\d+)" '
                 r'width="(\d+)" height="(\d+)"', xml)}
    w, h, px = BU.load_atlas_rgba(base + ".png")
    return rects, w, h, px


def cut(rects, aw, apx, name):
    sx, sy, sw, sh = rects[name]
    sub = bytearray(sw * sh * 4)
    BU.blit(sub, sw, apx, aw, sx, sy, sw, sh, 0, 0)
    return sub, sw, sh


def hueshift_banner(px, w, h):
    """Banner variant: the select-screen banner is mostly a dark navy FILL, so
    the player-sheet mapping (which preserves lightness) turns it olive-brown.
    Lift lightness hard so the cloth reads as gold at a glance."""
    out = bytearray(px)
    for i in range(0, w * h * 4, 4):
        r, g, b, a = out[i], out[i + 1], out[i + 2], out[i + 3]
        if a == 0:
            continue
        hh, ll, ss = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
        deg = hh * 360
        if 150 <= deg <= 280 and ss > 0.10:
            nh = (44 + 12 * min(ll, 0.7)) / 360
            ns = min(1.0, ss * 1.2 + 0.35)
            nl = min(0.95, 0.30 + 0.75 * ll)
            nr, ng, nb = colorsys.hls_to_rgb(nh, nl, ns)
            out[i], out[i + 1], out[i + 2] = int(nr * 255), int(ng * 255), int(nb * 255)
    return out


def sanitize(name):
    return name.replace("/", "_")


def mirror(px, w, h):
    """Flip horizontally, so the gold banner is not a pure recolour of blue's."""
    out = bytearray(len(px))
    for y in range(h):
        row = y * w * 4
        for x in range(w):
            src = row + x * 4
            dst = row + (w - 1 - x) * 4
            out[dst:dst + 4] = px[src:src + 4]
    return out


def gen_banner():
    rects, aw, ah, apx = read_atlas(MENU_ATLAS)
    sub, sw, sh = cut(rects, aw, apx, "teamA2x")
    write_png(os.path.join(OUT, "menu", "teamC2x.png"), sw, sh,
              mirror(hueshift_banner(sub, sw, sh), sw, sh))
    print(f"banner: menu/teamC2x.png {sw}x{sh} (gold, mirrored)")


# Mode icon: the vanilla icons are flat navy silhouettes. Colorize the Team
# Deathmatch silhouette into three vertical bands (blue | gold | red) so the
# carousel entry reads as "three teams" at a glance, keeping the original
# per-pixel luminance so the art still has its shading.
BANDS = ((0x3C, 0xBC, 0xFC), (0xFC, 0xE8, 0x3C), (0xC8, 0x4B, 0x2E))


def gen_mode_icon():
    rects, aw, ah, apx = read_atlas(MENU_ATLAS)
    sub, sw, sh = cut(rects, aw, apx, "gameModes/teamDeathmatch")
    out = bytearray(sub)
    for y in range(sh):
        for x in range(sw):
            i = (y * sw + x) * 4
            a = out[i + 3]
            if a == 0:
                continue
            base = BANDS[min(2, x * 3 // sw)]
            lum = (out[i] * 0.30 + out[i + 1] * 0.59 + out[i + 2] * 0.11) / 255.0
            # navy silhouette sits around lum 0.15 -> map to a solid band color,
            # brighter interior pixels stay brighter.
            k = 0.55 + 1.6 * lum
            for c in range(3):
                out[i + c] = max(0, min(255, int(base[c] * k)))
    write_png(os.path.join(OUT, "gameModes", "threeteam.png"), sw, sh, out)
    print(f"mode icon: gameModes/threeteam.png {sw}x{sh}")


if __name__ == "__main__":
    for p in (MENU_ATLAS + ".xml",):
        if not os.path.exists(p):
            sys.exit(f"missing game file: {p}")
    gen_banner()
    gen_mode_icon()
    print("assets ->", OUT)
