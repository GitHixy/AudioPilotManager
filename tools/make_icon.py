"""
Generates Audio Pilot Manager's logo and icons from one set of constants.

The mark is three mixer faders on a deep indigo squircle. Their knobs climb from left to
right, and a flight path threads through them and takes off towards the top-right corner:
a mixer with a pilot. The accent gradient (violet to cyan) is the app's default "Aurora"
accent in src/AudioPilotManager/Themes/Theme.cs.

Outputs:
  assets/logo.png                              512x512 logo (README, releases)
  assets/banner.png                            1280x640 social / README banner
  src/AudioPilotManager/Assets/app.ico         multi-size Windows icon (16..256)
  src/AudioPilotManager/Assets/logo.png        256x256 logo used inside the app

Run from the repository root:  python tools/make_icon.py
Needs Pillow and NumPy.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# ---------------------------------------------------------------- palette ------
BG_TOP = (0x24, 0x1D, 0x5C)
BG_BOTTOM = (0x09, 0x0B, 0x1A)
VIOLET = (0x8B, 0x5C, 0xF6)
CYAN = (0x22, 0xD3, 0xEE)
WHITE = (0xFF, 0xFF, 0xFF)

# ---------------------------------------------------------------- geometry -----
# Everything is in units of the icon's side (0..1) and drawn at SS times the target size.
SS = 4
CORNER = 0.225
TRACK_X = (0.30, 0.50, 0.70)
TRACK_TOP, TRACK_BOTTOM = 0.24, 0.78
KNOB_Y = (0.63, 0.49, 0.37)
PATH_START = (0.12, 0.80)
PATH_END = (0.86, 0.17)


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def accent_at(t):
    return lerp(VIOLET, CYAN, max(0.0, min(1.0, t)))


def gradient_image(w, h, c0, c1, vertical=True):
    t = np.linspace(0.0, 1.0, h if vertical else w, dtype=np.float32)
    c0 = np.array(c0, np.float32)
    c1 = np.array(c1, np.float32)
    line = c0[None, :] + (c1 - c0)[None, :] * t[:, None]
    if vertical:
        arr = np.repeat(line[:, None, :], w, axis=1)
    else:
        arr = np.repeat(line[None, :, :], h, axis=0)
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGB")


def diagonal_accent(w, h):
    """Violet at the bottom-left, cyan at the top-right."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = ((xx / max(w - 1, 1)) + (1.0 - yy / max(h - 1, 1))) / 2.0
    c0 = np.array(VIOLET, np.float32)
    c1 = np.array(CYAN, np.float32)
    arr = c0[None, None, :] + (c1 - c0)[None, None, :] * t[:, :, None]
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGB")


def squircle_mask(size, corner):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, size - 1, size - 1), radius=int(size * corner), fill=255)
    return m


def bezier(p0, p1, p2, p3, n=200):
    pts = []
    for i in range(n + 1):
        t = i / n
        a = (1 - t) ** 3
        b = 3 * (1 - t) ** 2 * t
        c = 3 * (1 - t) * t ** 2
        d = t ** 3
        pts.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0],
                    a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return pts


def flight_path():
    """A smooth curve from bottom-left, through the three knobs, out to the top-right."""
    k = [(TRACK_X[i], KNOB_Y[i]) for i in range(3)]
    pts = []
    pts += bezier(PATH_START, (0.20, 0.74), (0.24, 0.66), k[0], 60)
    pts += bezier(k[0], (0.37, 0.60), (0.43, 0.51), k[1], 60)[1:]
    pts += bezier(k[1], (0.57, 0.47), (0.63, 0.40), k[2], 60)[1:]
    pts += bezier(k[2], (0.76, 0.34), (0.80, 0.25), PATH_END, 60)[1:]
    return pts


def draw_mark(size, detailed=True):
    """Renders the icon at `size` pixels (RGBA)."""
    S = size * SS
    u = lambda v: v * S  # noqa: E731

    # Background: vertical gradient with a violet glow top-left and cyan glow bottom-right.
    bg = gradient_image(S, S, BG_TOP, BG_BOTTOM).convert("RGBA")
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((u(-0.25), u(-0.30), u(0.65), u(0.55)), fill=VIOLET + (110,))
    gd.ellipse((u(0.55), u(0.55), u(1.25), u(1.25)), fill=CYAN + (60,))
    glow = glow.filter(ImageFilter.GaussianBlur(u(0.16)))
    bg = Image.alpha_composite(bg, glow)

    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    accent = diagonal_accent(S, S).convert("RGBA")

    track_w = u(0.05 if detailed else 0.085)
    knob_w, knob_h = u(0.15 if detailed else 0.2), u(0.07 if detailed else 0.1)

    # Tracks (dim) and their filled part below each knob (accent).
    tracks = Image.new("L", (S, S), 0)
    fills = Image.new("L", (S, S), 0)
    td, fd = ImageDraw.Draw(tracks), ImageDraw.Draw(fills)
    for x, ky in zip(TRACK_X, KNOB_Y):
        box = (u(x) - track_w / 2, u(TRACK_TOP), u(x) + track_w / 2, u(TRACK_BOTTOM))
        td.rounded_rectangle(box, radius=track_w / 2, fill=255)
        fd.rounded_rectangle((box[0], u(ky), box[2], box[3]), radius=track_w / 2, fill=255)
    layer.paste((255, 255, 255, 38), (0, 0), tracks)
    layer.paste(accent, (0, 0), fills)

    # Flight path, with a soft glow, drawn behind the knobs.
    if detailed:
        pts = [(u(x), u(y)) for x, y in flight_path()]
        path_mask = Image.new("L", (S, S), 0)
        ImageDraw.Draw(path_mask).line(pts, fill=255, width=int(u(0.022)), joint="curve")
        # Arrow head at the end of the path.
        (x1, y1), (x2, y2) = pts[-8], pts[-1]
        ang = math.atan2(y2 - y1, x2 - x1)
        head = u(0.075)
        tip = (x2 + math.cos(ang) * head * 0.55, y2 + math.sin(ang) * head * 0.55)
        left = (x2 + math.cos(ang + 2.45) * head, y2 + math.sin(ang + 2.45) * head)
        right = (x2 + math.cos(ang - 2.45) * head, y2 + math.sin(ang - 2.45) * head)
        ImageDraw.Draw(path_mask).polygon([tip, left, (x2, y2), right], fill=255)
        path_glow = path_mask.filter(ImageFilter.GaussianBlur(u(0.025)))
        glow_layer = Image.new("RGBA", (S, S), CYAN + (0,))
        glow_layer.putalpha(path_glow.point(lambda v: int(v * 0.8)))
        layer = Image.alpha_composite(layer, glow_layer)
        path_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        path_layer.paste(accent, (0, 0), path_mask)
        # Brighten the path towards white so it reads above the fills.
        white = Image.new("RGBA", (S, S), WHITE + (0,))
        white.putalpha(path_mask.point(lambda v: int(v * 0.35)))
        layer = Image.alpha_composite(layer, path_layer)
        layer = Image.alpha_composite(layer, white)

    # Knobs: white pills with an accent glow.
    knobs = Image.new("L", (S, S), 0)
    kd = ImageDraw.Draw(knobs)
    for x, ky in zip(TRACK_X, KNOB_Y):
        kd.rounded_rectangle((u(x) - knob_w / 2, u(ky) - knob_h / 2, u(x) + knob_w / 2, u(ky) + knob_h / 2),
                             radius=knob_h / 2, fill=255)
    if detailed:
        kglow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        kglow.paste(accent, (0, 0), knobs.filter(ImageFilter.GaussianBlur(u(0.03))))
        layer = Image.alpha_composite(layer, kglow)
        # A dark rim separates the knob from the path passing through it.
        rim = knobs.filter(ImageFilter.MaxFilter(int(u(0.012)) | 1))
        layer.paste((12, 12, 32, 255), (0, 0), rim)
    layer.paste((255, 255, 255, 255), (0, 0), knobs)

    img = Image.alpha_composite(bg, layer)

    # Hairline highlight around the squircle edge.
    if detailed:
        edge = Image.new("L", (S, S), 0)
        ImageDraw.Draw(edge).rounded_rectangle((u(0.004), u(0.004), S - u(0.004), S - u(0.004)),
                                               radius=int(S * CORNER), outline=255, width=max(1, int(u(0.006))))
        hl = Image.new("RGBA", (S, S), WHITE + (0,))
        hl.putalpha(edge.point(lambda v: int(v * 0.16)))
        img = Image.alpha_composite(img, hl)

    img.putalpha(squircle_mask(S, CORNER))
    return img.resize((size, size), Image.LANCZOS)


def find_font(bold=False):
    candidates = ["C:/Windows/Fonts/SegUIVar.ttf", "C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
                  "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"]
    for c in candidates:
        if os.path.exists(c):
            return c
    return None


def load_font(path, size, weight):
    font = ImageFont.truetype(path, size)
    try:
        font.set_variation_by_axes([weight])
    except Exception:
        pass
    return font


def draw_banner(logo):
    W, H = 1280, 640
    img = gradient_image(W, H, (0x14, 0x11, 0x33), (0x07, 0x08, 0x12)).convert("RGBA")
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse((-200, -300, 700, 500), fill=VIOLET + (90,))
    gd.ellipse((800, 300, 1500, 900), fill=CYAN + (55,))
    img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(140)))

    # Faint mixer grid lines, like fader tracks in the distance.
    lines = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ld = ImageDraw.Draw(lines)
    for i, x in enumerate(range(1030, 1250, 36)):
        h = 160 + int(120 * (0.5 + 0.5 * math.sin(i * 0.9)))
        ld.rounded_rectangle((x, 500 - h, x + 8, 500), radius=4, fill=WHITE + (16,))
        ld.rounded_rectangle((x, 500 - int(h * 0.55), x + 8, 500), radius=4, fill=accent_at(i / 6) + (80,))
    img = Image.alpha_composite(img, lines)

    lg = logo.resize((300, 300), Image.LANCZOS)
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sm = Image.new("L", (W, H), 0)
    sm.paste(lg.getchannel("A"), (110, 180))
    shadow.putalpha(sm.filter(ImageFilter.GaussianBlur(30)).point(lambda v: int(v * 0.6)))
    img = Image.alpha_composite(img, shadow)
    img.alpha_composite(lg, (110, 160))

    path = find_font(bold=True)
    d = ImageDraw.Draw(img)
    if path:
        title = load_font(path, 76, 700)
        sub = load_font(find_font(), 30, 400)
        d.text((460, 222), "Audio Pilot", font=title, fill=WHITE)
        d.text((460, 305), "Manager", font=title, fill=accent_at(0.7))
        d.text((464, 408), "Every app, every device, every channel.", font=sub, fill=(0xC6, 0xC9, 0xDB))
        d.text((464, 450), "One modern mixer for Windows 10 & 11.", font=sub, fill=(0x8E, 0x93, 0xAB))
    return img.convert("RGB")


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    assets = os.path.join(root, "assets")
    app_assets = os.path.join(root, "src", "AudioPilotManager", "Assets")
    os.makedirs(assets, exist_ok=True)
    os.makedirs(app_assets, exist_ok=True)

    logo = draw_mark(512)
    logo.save(os.path.join(assets, "logo.png"))
    draw_mark(256).save(os.path.join(app_assets, "logo.png"))
    draw_banner(logo).save(os.path.join(assets, "banner.png"))

    # Small sizes use the simplified mark (no path, bolder shapes) so they stay crisp.
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [draw_mark(s, detailed=s >= 40) for s in sizes]
    frames[-1].save(os.path.join(app_assets, "app.ico"), format="ICO",
                    sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    print("Wrote assets/logo.png, assets/banner.png, Assets/app.ico, Assets/logo.png")


if __name__ == "__main__":
    main()
