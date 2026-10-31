# SPDX-License-Identifier: MPL-2.0
"""Prepare only the packed sleeping-mat sprite. / 仅处理已打包睡垫贴图。"""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "ArtSource" / "PortableSleepingPadPacked.generated.png"
OUTPUT = ROOT / "Mod" / "Textures" / "FAF" / "Things" / "PortableSleepingPadPacked.png"


def main() -> None:
    # EN: The item itself is generated artwork; processing only cleans alpha, crops and resizes.
    # ZH: 物品主体来自生成画稿；本处理仅清理透明边、裁切和缩放。
    with Image.open(SOURCE) as image:
        original = image.convert("RGBA")
    alpha = original.getchannel("A").point(lambda value: 0 if value < 16 else value)
    original.putalpha(alpha)
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError("Packed mattress source is empty. / 打包睡垫原画为空。")
    artwork = original.crop(bounds)
    artwork.thumbnail((224, 224), Image.Resampling.LANCZOS)

    # EN: Fit one item on a square tile, keeping a sixteen-pixel margin on its longest edge.
    # ZH: 单件物品居中放入方形贴图，最长边两端保留十六像素边距。
    packed = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    packed.alpha_composite(artwork, ((256 - artwork.width) // 2, (256 - artwork.height) // 2))
    packed.info.clear()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    packed.save(OUTPUT, optimize=True)
    print(f"Mod/Textures/FAF/Things/PortableSleepingPadPacked.png: 256x256 RGBA, {OUTPUT.stat().st_size} bytes")


if __name__ == "__main__":
    main()
