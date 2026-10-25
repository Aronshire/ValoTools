"""Render the branded Inno Setup welcome art from the application logo."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
image = Image.new("RGB", (360, 700), "#10181C")
draw = ImageDraw.Draw(image)

for y in range(700):
    t = y / 699
    color = (round(15 + t * 4), round(24 + t * 10), round(28 + t * 12))
    draw.line((0, y, 359, y), fill=color)

draw.polygon([(0, 380), (360, 110), (360, 390), (0, 660)], fill="#162D32")
draw.polygon([(0, 555), (360, 285), (360, 344), (0, 614)], fill="#1A3A40")
draw.line((28, 650, 332, 650), fill="#367D85", width=3)
draw.rectangle((28, 650, 72, 653), fill="#43C4CD")

logo = Image.open(ROOT.parent / "assets" / "ValoTools.png").convert("RGBA")
logo = logo.resize((188, 188), Image.Resampling.LANCZOS)
image.paste(logo, (86, 108), logo)

font_dir = Path("C:/Windows/Fonts")
title_font = ImageFont.truetype(str(font_dir / "segoeuib.ttf"), 41)
subtitle_font = ImageFont.truetype(str(font_dir / "segoeui.ttf"), 17)
draw = ImageDraw.Draw(image)
title = "VALOTOOLS"
subtitle = "VALORANT TOOLS"
title_width = draw.textbbox((0, 0), title, font=title_font)[2]
subtitle_width = draw.textbbox((0, 0), subtitle, font=subtitle_font)[2]
draw.text(((360 - title_width) / 2, 337), title, font=title_font, fill="#F3FAFA")
draw.text(((360 - subtitle_width) / 2, 397), subtitle, font=subtitle_font, fill="#76B8BE")

image.save(ROOT / "wizard-side.png")
