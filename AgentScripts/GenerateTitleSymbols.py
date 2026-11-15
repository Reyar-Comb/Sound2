"""Generate the original, angular title ampersand (fonttools + shapely required).

Matches SECONDERSIGN's 2048 UPM, 1460 advance, -246..2020 cap bounds
and 218-unit stem width. Only this symbol is replaced, not the theme font.
After regenerating, rebake '&' in the SoundShellSymbols TMP font asset.
"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Logs/font-tools"))
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from shapely.geometry import LineString
from shapely.geometry.polygon import orient

# Flat terminals and mitered corners; the crossing and two open counters keep
# the symbol recognizable while matching the condensed, geometric letters.
spine = [(1242, -137), (350, 1100), (260, 1280), (260, 1770),
         (401, 1911), (830, 1911), (971, 1770), (971, 1390),
         (218, 530), (218, 20), (375, -137), (785, -137), (1242, 680)]
outline = orient(LineString(spine).buffer(109, cap_style=2, join_style=2), sign=-1)
pen = TTGlyphPen(None)
for ring in [outline.exterior, *outline.interiors]:
    points = [(round(x), round(y)) for x, y in list(ring.coords)[:-1]]
    pen.moveTo(points[0])
    for point in points[1:]:
        pen.lineTo(point)
    pen.closePath()

builder = FontBuilder(2048, isTTF=True)
builder.setupGlyphOrder([".notdef", "space", "ampersand"])
builder.setupCharacterMap({32: "space", 38: "ampersand"})
empty = TTGlyphPen(None).glyph()
builder.setupGlyf({".notdef": empty, "space": empty, "ampersand": pen.glyph()})
builder.setupHorizontalMetrics({".notdef": (1460, 0), "space": (400, 0),
                                "ampersand": (1460, round(outline.bounds[0]))})
builder.setupHorizontalHeader(ascent=2122, descent=-362)
builder.setupNameTable({"familyName": "SoundShellSymbols", "styleName": "Regular",
                       "uniqueFontIdentifier": "SoundShellSymbols-Regular-1.0",
                       "fullName": "SoundShellSymbols Regular", "psName": "SoundShellSymbols-Regular",
                       "version": "Version 1.0"})
builder.setupOS2(sTypoAscender=2122, sTypoDescender=-362, usWinAscent=2122,
                 usWinDescent=362, sCapHeight=2020, sxHeight=996)
builder.setupPost()
builder.save(ROOT / "Assets/UI/SoundShellSymbols.ttf")
