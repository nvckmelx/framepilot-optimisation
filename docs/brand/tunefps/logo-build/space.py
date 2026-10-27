"""Optical spacing for the 'tunefps' wordmark in Archivo Black.

Rasterizes each glyph from its outline, measures the white area between
neighbours inside the x-height band (with a depth clamp, as type designers
do), and finds the offset for each pair that makes its area equal to the
straight-stem reference pair u|n. Writes kern.json with offsets in font units.
"""
import json
import sys

from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from PIL import Image, ImageDraw
from fontTools.pens.basePen import BasePen

FONT = sys.argv[1]
WORD = "tunefps"
S = 0.5  # raster scale: px per font unit

font = TTFont(FONT)
gs = font.getGlyphSet()
cmap = font.getBestCmap()
upm = font["head"].unitsPerEm
os2 = font["OS/2"]
xh = os2.sxHeight
asc = font["hhea"].ascent
desc = font["hhea"].descent


class PolyPen(BasePen):
    """Flattens outlines into polygons."""

    def __init__(self, glyphSet):
        super().__init__(glyphSet)
        self.polys, self.cur = [], []

    def _moveTo(self, p):
        self.cur = [p]

    def _lineTo(self, p):
        self.cur.append(p)

    def _curveToOne(self, p1, p2, p3):
        p0 = self.cur[-1]
        for i in range(1, 17):
            t = i / 16
            mt = 1 - t
            self.cur.append((
                mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0],
                mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1],
            ))

    def _qCurveToOne(self, p1, p2):
        p0 = self.cur[-1]
        for i in range(1, 17):
            t = i / 16
            mt = 1 - t
            self.cur.append((mt * mt * p0[0] + 2 * mt * t * p1[0] + t * t * p2[0],
                             mt * mt * p0[1] + 2 * mt * t * p1[1] + t * t * p2[1]))

    def _closePath(self):
        if self.cur:
            self.polys.append(self.cur)
        self.cur = []


def raster(ch):
    """Return (mask, advance) where mask rows run from y=asc down to y=desc."""
    name = cmap[ord(ch)]
    g = gs[name]
    pen = PolyPen(gs)
    g.draw(pen)
    w = int((g.width + 400) * S)
    h = int((asc - desc) * S)
    img = Image.new("1", (w, h), 0)
    d = ImageDraw.Draw(img)
    # even-odd fill of all contours (XOR) handles counters
    for poly in pen.polys:
        layer = Image.new("1", (w, h), 0)
        ImageDraw.Draw(layer).polygon([((x + 200) * S, (asc - y) * S) for x, y in poly], fill=1)
        img = Image.frombytes("1", (w, h), bytes(a ^ b for a, b in zip(img.tobytes(), layer.tobytes())))
    return img, g.width


glyphs = {c: raster(c) for c in set(WORD)}


def profile(ch, side):
    """Per raster row: rightmost (side='r') or leftmost (side='l') ink x, in font units relative to origin."""
    img, adv = glyphs[ch]
    w, h = img.size
    px = img.load()
    out = []
    for yy in range(h):
        xs = [x for x in range(w) if px[x, yy]]
        if not xs:
            out.append(None)
        else:
            v = (xs[-1] if side == "r" else xs[0]) / S - 200
            out.append(v)
    return out


prof = {c: (profile(c, "l"), profile(c, "r")) for c in set(WORD)}
top = int((asc - xh) * S)          # x-height row
bot = int(asc * S)                 # baseline row
band = range(top, bot)
DEPTH = xh * 0.28                  # clamp: how deep into an open side the eye "sees"


def area(a, b, offset):
    """White area between glyph a and b (b starts at a.advance + offset), within the band."""
    _, adv = glyphs[a]
    ra = prof[a][1]
    lb = prof[b][0]
    tot = 0.0
    for yy in band:
        right = ra[yy]
        left = lb[yy]
        r = right if right is not None else -1e9
        l = (left + adv + offset) if left is not None else 1e9
        gap = l - r
        tot += max(0.0, min(gap, DEPTH)) if (right is not None and left is not None) else DEPTH
    return tot / S


def min_gap(a, b, offset):
    """Smallest horizontal clearance between the two outlines over the full height."""
    _, adv = glyphs[a]
    ra, lb = prof[a][1], prof[b][0]
    gaps = [lb[y] + adv + offset - ra[y] for y in range(len(ra)) if ra[y] is not None and lb[y] is not None]
    return min(gaps) if gaps else None


ref = area("u", "n", 0)
pairs = [WORD[i:i + 2] for i in range(len(WORD) - 1)]
result = {}
for p in pairs:
    lo, hi = -300.0, 300.0
    for _ in range(40):
        mid = (lo + hi) / 2
        if area(p[0], p[1], mid) < ref:
            lo = mid
        else:
            hi = mid
    off = round((lo + hi) / 2)
    result[p] = {"offset": off, "default_area": round(area(p[0], p[1], 0)), "min_gap_after": round(min_gap(p[0], p[1], off))}

info = {"upm": upm, "xHeight": xh, "ascent": asc, "descent": desc,
        "advances": {c: glyphs[c][1] for c in set(WORD)}, "ref_area_un": round(ref), "pairs": result}
json.dump(info, open("kern.json", "w"), indent=1)
print(json.dumps(info, indent=1))
