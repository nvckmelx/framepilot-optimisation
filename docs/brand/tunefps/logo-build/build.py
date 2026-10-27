"""Build the outlined TuneFPS logo (SVG paths) from Archivo Black + kern.json.

usage: python build.py FONT TRACKING OUTDIR
(Reconstruction of the brand chat's build.py at its final state: MIN_GAP ef 62, fp 40, default 28;
icon scaled to the ascender; PNG preview omitted.)
"""
import json
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.boundsPen import BoundsPen

FONT, TRACK, OUT = sys.argv[1], int(sys.argv[2]), sys.argv[3]
os.makedirs(OUT, exist_ok=True)
WORD = "tunefps"
font = TTFont(FONT)
gs = font.getGlyphSet()
cmap = font.getBestCmap()
k = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "kern.json")))
XH, ASC = k["xHeight"], k["ascent"]

MIN_GAP = {"ef": 62, "fp": 40}
pos, x = [], 0
for i, ch in enumerate(WORD):
    if i > 0:
        pair = WORD[i - 1:i + 1]
        pk = k["pairs"][pair]
        off = pk["offset"] + TRACK
        gap0 = pk["min_gap_after"] - pk["offset"]
        need = MIN_GAP.get(pair, 28)
        if gap0 + off < need:
            off = need - gap0
        print(f"{pair}: offset {off:+d}, clearance {gap0 + off}")
        x += off
    pos.append(x)
    x += gs[cmap[ord(ch)]].width

xmin = ymin = 1e9
xmax = ymax = -1e9
for ch, px in zip(WORD, pos):
    bp = BoundsPen(gs)
    gs[cmap[ord(ch)]].draw(TransformPen(bp, (1, 0, 0, 1, px, 0)))
    if bp.bounds:
        a, b, c, d = bp.bounds
        xmin, ymin, xmax, ymax = min(xmin, a), min(ymin, b), max(xmax, c), max(ymax, d)


def glyph_path(ch, px):
    sp = SVGPathPen(gs)
    gs[cmap[ord(ch)]].draw(TransformPen(sp, (1, 0, 0, -1, px - xmin, ymax)))
    return sp.getCommands()


word_w, word_h = xmax - xmin, ymax - ymin
paths = [glyph_path(c, p) for c, p in zip(WORD, pos)]
tune_d = " ".join(paths[:4])
fps_d = " ".join(paths[4:])

ICON_SCALE = ymax / 64
GAP = XH * 0.42


def icon_svg(tile, bolt, x0, y0, s):
    return (f'<g transform="translate({x0:.1f} {y0:.1f}) scale({s:.4f})">'
            f'<rect width="64" height="64" rx="14" fill="{tile}"/>'
            f'<polygon points="37,10 17,36 30,36 25,54 47,27 34,27 39,10" fill="{bolt}"/></g>')


def lockup(tune_c, fps_c, tile, bolt, bg=None, pad=0):
    icon_w = 64 * ICON_SCALE
    W = icon_w + GAP + word_w + pad * 2
    H = word_h + pad * 2
    body = []
    if bg:
        body.append(f'<rect width="{W:.0f}" height="{H:.0f}" fill="{bg}"/>')
    body.append(icon_svg(tile, bolt, pad, pad, ICON_SCALE))
    tx = pad + icon_w + GAP
    body.append(f'<g transform="translate({tx:.1f} {pad})"><path fill="{tune_c}" d="{tune_d}"/><path fill="{fps_c}" d="{fps_d}"/></g>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W:.0f} {H:.0f}" width="{W:.0f}" height="{H:.0f}">'
            + "".join(body) + "</svg>")


def wordmark_only(tune_c, fps_c):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {word_w:.0f} {word_h:.0f}">'
            f'<path fill="{tune_c}" d="{tune_d}"/><path fill="{fps_c}" d="{fps_d}"/></svg>')


BLUE, STEEL, MID = "#007BFF", "#A3B3C6", "#1A1C20"
files = {
    "tunefps-logo-dark.svg": lockup("#FFFFFF", STEEL, BLUE, "#FFFFFF"),
    "tunefps-logo-light.svg": lockup(MID, BLUE, BLUE, "#FFFFFF"),
    "tunefps-logo-mono-white.svg": lockup("#FFFFFF", "#FFFFFF", "#FFFFFF", "#000000"),
    "tunefps-logo-mono-black.svg": lockup("#000000", "#000000", "#000000", "#FFFFFF"),
    "tunefps-wordmark-dark.svg": wordmark_only("#FFFFFF", STEEL),
    "tunefps-icon.svg": '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="14" fill="#007BFF"/><polygon points="37,10 17,36 30,36 25,54 47,27 34,27 39,10" fill="#FFFFFF"/></svg>',
}
for n, s in files.items():
    open(os.path.join(OUT, n), "w").write(s)
print("word ink box", round(word_w), "x", round(word_h), "positions", pos)
