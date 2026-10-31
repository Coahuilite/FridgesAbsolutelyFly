# SPDX-License-Identifier: MPL-2.0
"""Build the landscape preview and small mod icon. / 制作横向封面与小尺寸模组图标。"""

import argparse
import shutil
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ABOUT = ROOT / "Mod" / "About"


def build_preview() -> None:
    # EN: This alias holds the accepted, already-finished independent three-stage cover.
    # ZH: 此源文件是已接受且已完成尺寸/压缩处理的独立三阶段封面副本。
    # EN: Publish it unchanged; resizing, palette conversion or re-encoding would change the accepted file.
    # ZH: 原样发布；不再缩放、转换调色板或重新编码已接受文件。
    shutil.copyfile(ROOT / "ArtSource" / "Preview.generated.png", ABOUT / "Preview.png")


def build_icon() -> None:
    # EN: Crop the freezer below the arrow from the master-referenced Fly generation: same model, no arrow.
    # ZH: 从引用母图生成的 Fly 画稿中裁出箭头下方的冷柜：同一型号，不保留箭头。
    with Image.open(ROOT / "ArtSource" / "CommandFly.generated.png") as source:
        original = source.convert("RGBA").crop((0, 431, 1254, 1254))
    alpha = original.getchannel("A").point(lambda value: 0 if value < 16 else value)
    original.putalpha(alpha)
    original.info.clear()
    original.save(ROOT / "ArtSource" / "ModIcon.derived.png", optimize=True)
    bounds = original.getchannel("A").getbbox()
    if bounds is None:
        raise ValueError("ModIcon source is transparent. / ModIcon 原画完全透明。")
    freezer = original.crop(bounds)
    freezer.thumbnail((60, 60), Image.Resampling.LANCZOS)

    # EN: Preserve alpha and aspect ratio, leaving a two-pixel safety margin at the longest edge.
    # ZH: 保留透明通道与纵横比，在最长边两端留出两像素安全边距。
    icon = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    icon.alpha_composite(freezer, ((64 - freezer.width) // 2, (64 - freezer.height) // 2))
    icon.info.clear()
    icon.save(ABOUT / "ModIcon.png", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview-only", action="store_true", help="Preserve ModIcon / 保持 ModIcon 不动")
    args = parser.parse_args()
    ABOUT.mkdir(parents=True, exist_ok=True)
    build_preview()
    outputs = ["Preview.png"]
    if not args.preview_only:
        build_icon()
        outputs.append("ModIcon.png")
    for name in outputs:
        path = ABOUT / name
        with Image.open(path) as image:
            print(f"Mod/About/{name}: {image.width}x{image.height}, {path.stat().st_size} bytes, {image.mode}")


if __name__ == "__main__":
    main()
