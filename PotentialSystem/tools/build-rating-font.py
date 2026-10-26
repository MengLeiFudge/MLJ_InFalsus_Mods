"""从 APK 导出的 Exo 字体生成 PTT 字形图集；仅用于构建素材，不参与游戏运行。"""
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
FONT = ROOT / "Assets/Fonts/Exo-SemiBold.ttf"
SCALE = 4
STROKE = 3
CHARACTERS = "0123456789.-"
# Arcaea 7.0.255c 的原始字体；防止误用系统安装的同名字体。
FONT_SHA256 = "d97a2e537fa675e7bf1f0d50d370378f52a9a2629dd2f065ce67bbc58c88b338"


def main():
    """按 UserCell.csb 的 34/24 字号与原生代码的 3 像素描边生成可复现素材。"""
    if hashlib.sha256(FONT.read_bytes()).hexdigest() != FONT_SHA256:
        raise ValueError("Exo-SemiBold.ttf 与已核实的 APK 字体不一致")
    atlas = Image.new("RGBA", (2048, 512))
    draw = ImageDraw.Draw(atlas)
    faces = {}
    for row, size in enumerate((34, 24)):
        font = ImageFont.truetype(str(FONT), size * SCALE)
        glyphs = {}
        x, y = 0, row * 256
        for character in CHARACTERS:
            left, top, right, bottom = font.getbbox(character, anchor="ls", stroke_width=STROKE * SCALE)
            width, height = right - left, bottom - top
            if x + width > atlas.width or height > 256:
                raise ValueError("字形超出图集范围")
            # R 保存白色字面的覆盖比例；黑色描边由运行时按 PTT 档位着色。
            draw.text((x - left, y - top), character, font=font, anchor="ls",
                      fill="white", stroke_width=STROKE * SCALE, stroke_fill="black")
            glyphs[character] = dict(X=x, Y=y, Width=width, Height=height, Left=left, Top=top,
                                     Advance=font.getlength(character))
            x += width + 2
        kerning = {}
        for first in CHARACTERS:
            for second in CHARACTERS:
                delta = font.getlength(first + second) - font.getlength(first) - font.getlength(second)
                if delta:
                    kerning[first + second] = delta
        faces[str(size)] = dict(Glyphs=glyphs, Kerning=kerning)
    atlas.save(ROOT / "Assets/Fonts/RatingGlyphs.png")
    metadata = dict(Scale=SCALE, Stroke=STROKE, FontSha256=FONT_SHA256, Faces=faces)
    (ROOT / "Assets/Fonts/RatingGlyphs.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
