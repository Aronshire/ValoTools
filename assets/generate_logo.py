"""Render the ValoTools vector mark to PNG and multi-size Windows ICO."""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).parent
SCALE = 4
SIZE = 256


def points(coords):
    return [(round(x * SCALE), round(y * SCALE)) for x, y in coords]


image = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(
    (8 * SCALE, 8 * SCALE, 248 * SCALE, 248 * SCALE),
    radius=55 * SCALE,
    fill="#10181C",
    outline="#31565D",
    width=3 * SCALE,
)
draw.polygon(points([(30, 155), (143, 42), (226, 42), (75, 193)]), fill="#152C32")
draw.polygon(points([(48, 69), (80, 69), (128, 168), (112, 201)]), fill="#EFF8F8")
draw.polygon(points([(177, 69), (208, 69), (144, 201), (112, 201)]), fill="#39BAC5")
draw.polygon(points([(175, 51), (202, 51), (197, 61), (170, 61)]), fill="#F37179")
draw.line(points([(40, 76), (40, 50), (66, 50)]), fill="#477D84", width=3 * SCALE, joint="curve")
draw.line(points([(190, 207), (216, 207), (216, 181)]), fill="#477D84", width=3 * SCALE, joint="curve")

logo = image.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
logo.save(ROOT / "ValoTools.png")
logo.save(ROOT / "ValoTools.ico", format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
