# -*- coding: utf-8 -*-
"""
Generates Ghozt's branding assets from the Capital-Iz master mark.

Run:  python build-brand-assets.py

Why this is a script rather than a folder of hand-made images: the installer chrome has to carry the
same signature as CommandDeck and Mouse n Key, and the WiX bitmap sizes are fixed expectations rather
than suggestions (493x58 banner, 493x312 dialog). Getting one wrong stretches or letterboxes the
artwork instead of failing, so the geometry is stated once, here, and the assets are regenerated
rather than retouched.

Outputs:
  msi/Assets/BannerTop.bmp          493x58   mark on the RIGHT
  msi/Assets/DialogBackground.bmp   493x312  mark left-of-centre
  ../../App/src/Ghozt.App/Assets/GhoztLogo.ico   multi-size application icon
"""
from __future__ import annotations

import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
MASTER_MARK = os.path.join(
    HERE, "..", "..", "..", "..", "CAPITAL-IZ Marketing", "public", "images",
    "capital-iz-logo-01.png")

MSI_ASSETS = os.path.join(HERE, "..", "msi", "Assets")
APP_ASSETS = os.path.join(HERE, "..", "..", "..", "App", "src", "Ghozt.App", "Assets")

# Ghozt's own identity, from GZT-UX-001 §03 (Spectral Black) and the semantic layer.
VOID = (0x05, 0x07, 0x0A)
SPECTRAL = (0x6E, 0xF2, 0xD6)
TELEMETRY = (0x55, 0xCF, 0xF5)
INK = (0x0F, 0x0F, 0x0F)
GREY = (0x8A, 0x8A, 0x8A)
PAPER = (0xFF, 0xFF, 0xFF)

FONT_BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
FONT_SEMI = r"C:\Windows\Fonts\seguisb.ttf"
FONT_LIGHT = r"C:\Windows\Fonts\segoeuil.ttf"


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


def mark(height: int, colour=None) -> Image.Image:
    """The Capital-Iz monogram, scaled to a height and optionally recoloured."""
    src = Image.open(MASTER_MARK).convert("RGBA")

    # Trim the transparent margin so 'height' means the height of the glyph itself rather than of
    # the square canvas it happens to sit in.
    bbox = src.split()[3].getbbox()
    src = src.crop(bbox)

    ratio = height / src.height
    src = src.resize((max(1, int(src.width * ratio)), height), Image.LANCZOS)

    if colour is not None:
        tint = Image.new("RGBA", src.size, colour + (255,))
        tint.putalpha(src.split()[3])
        return tint

    return src


def wordmark(draw: ImageDraw.ImageDraw, x: int, y: int, scale: float = 1.0,
             on_dark: bool = False) -> int:
    """Draws GHOZT plus its descriptor. Returns the width consumed."""
    primary = PAPER if on_dark else INK
    secondary = GREY if not on_dark else (0x9A, 0xA6, 0xB2)

    title = font(FONT_BOLD, int(21 * scale))
    text = "GHOZT"
    draw.text((x, y), text, font=title, fill=primary)
    width = int(draw.textlength(text, font=title))

    # The descriptor is formal metadata, never the product's personality - so it is small, spaced,
    # and quiet beneath the name.
    sub = font(FONT_SEMI, max(6, int(7.5 * scale)))
    descriptor = "D E V   T E S T E R"
    draw.text((x + 2, y + int(24 * scale)), descriptor, font=sub, fill=secondary)

    return max(width, int(draw.textlength(descriptor, font=sub)))


def build_banner() -> str:
    """493x58. The mark sits RIGHT because WiX draws each dialog's title over the banner's left."""
    image = Image.new("RGB", (493, 58), PAPER)
    draw = ImageDraw.Draw(image)

    glyph = mark(38)
    right_edge = 493 - 14

    title = font(FONT_BOLD, 17)
    name = "GHOZT"
    name_width = int(draw.textlength(name, font=title))

    sub = font(FONT_SEMI, 6)
    descriptor = "D E V   T E S T E R"
    sub_width = int(draw.textlength(descriptor, font=sub))

    text_width = max(name_width, sub_width)
    text_x = right_edge - text_width
    glyph_x = text_x - glyph.width - 9

    image.paste(glyph, (glyph_x, (58 - glyph.height) // 2), glyph)
    draw.text((text_x, 15), name, font=title, fill=INK)
    draw.text((text_x + 1, 35), descriptor, font=sub, fill=GREY)

    # A spectral hairline along the foot: Ghozt's own colour, at the family's restraint.
    draw.rectangle([0, 56, 492, 57], fill=SPECTRAL)

    path = os.path.join(MSI_ASSETS, "BannerTop.bmp")
    image.save(path)
    return path


def build_dialog() -> str:
    """493x312, behind the Welcome and Exit pages."""
    image = Image.new("RGB", (493, 312), PAPER)
    draw = ImageDraw.Draw(image)

    glyph = mark(96)
    glyph_x, glyph_y = 40, 104
    image.paste(glyph, (glyph_x, glyph_y), glyph)

    text_x = glyph_x + glyph.width + 16

    title = font(FONT_BOLD, 30)
    draw.text((text_x, glyph_y + 22), "GHOZT", font=title, fill=INK)

    sub = font(FONT_SEMI, 9)
    draw.text((text_x + 2, glyph_y + 62), "D E V   T E S T E R", font=sub, fill=GREY)

    house = font(FONT_LIGHT, 11)
    draw.text((text_x, glyph_y + 80), "A Capital-Iz system", font=house, fill=GREY)

    # The left spectral rail, echoing the shell's own selected-item treatment.
    draw.rectangle([0, 0, 3, 311], fill=SPECTRAL)

    path = os.path.join(MSI_ASSETS, "DialogBackground.bmp")
    image.save(path)
    return path


def build_icon() -> str:
    """
    The application icon: the Capital-Iz monogram in spectral teal on a chamfered void plate.

    The chamfer is Ghozt's own geometry (one clipped corner, GZT-UX-001 §04) and the colour is its
    signature. The monogram is the family's, unchanged - the product is Ghozt, the house is
    Capital-Iz, and the icon says both without inventing a second logo.
    """
    size = 512
    inset = 26
    radius = 96
    chamfer = 150
    stroke = 7

    box = [inset, inset, size - inset, size - inset]

    # The silhouette is built as a MASK rather than by painting and repainting the canvas.
    #
    # Cutting the corner out and drawing the diagonal back over the top leaves seams where the two
    # operations disagree by a pixel, and a rounded rectangle re-added to patch it reintroduces the
    # very corner the chamfer removed. One mask, cut once, has no seam to get wrong.
    silhouette = Image.new("L", (size, size), 0)
    ImageDraw.Draw(silhouette).rounded_rectangle(box, radius=radius, fill=255)

    corner = Image.new("L", (size, size), 0)
    ImageDraw.Draw(corner).polygon(
        [(inset - 2, inset - 2), (inset + chamfer, inset - 2), (inset - 2, inset + chamfer)],
        fill=255)
    silhouette.paste(0, (0, 0), corner)

    plate = Image.new("RGBA", (size, size), VOID + (255,))
    plate.putalpha(silhouette)

    # The edge is the silhouette minus an inward copy of itself, so it follows the chamfer exactly
    # instead of being three separate strokes that have to meet.
    inner = silhouette.resize(
        (size - stroke * 2, size - stroke * 2), Image.LANCZOS)
    shrunk = Image.new("L", (size, size), 0)
    shrunk.paste(inner, (stroke, stroke))

    edge = Image.new("L", (size, size), 0)
    edge.paste(silhouette)
    edge.paste(0, (0, 0), shrunk)

    outline = Image.new("RGBA", (size, size), SPECTRAL + (255,))
    outline.putalpha(edge)

    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    image.alpha_composite(plate)
    image.alpha_composite(outline)

    glyph = mark(268, SPECTRAL)
    image.alpha_composite(glyph, ((size - glyph.width) // 2, (size - glyph.height) // 2))

    os.makedirs(APP_ASSETS, exist_ok=True)
    path = os.path.join(APP_ASSETS, "GhoztLogo.ico")

    # Every size Windows asks for: Explorer, the taskbar, Alt-Tab, Add/Remove Programs and the
    # Start menu each pick a different one, and a missing size is upscaled badly rather than refused.
    image.save(path, sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])

    image.resize((256, 256), Image.LANCZOS).save(
        os.path.join(APP_ASSETS, "GhoztLogo-256.png"))

    return path


if __name__ == "__main__":
    os.makedirs(MSI_ASSETS, exist_ok=True)
    for produced in (build_banner(), build_dialog(), build_icon()):
        print("wrote", os.path.normpath(produced))
