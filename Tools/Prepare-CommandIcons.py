# SPDX-License-Identifier: MPL-2.0
"""Prepare generated command-icon rasters. / 处理生成的指令图标光栅画稿。"""

import argparse
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Mod" / "Textures" / "FAF" / "UI" / "Commands"
SOURCES = {
    "Fly": "CommandFly.generated.png",
    "InstallSleepingPad": "CommandInstallSleepingPad.generated.png",
    "LaunchCabinet": "CommandLaunchCabinet.generated.png",
    "NearFieldPulse": "CommandIcons/NearFieldPulse.generated.png",
    "RefillCoolant": "CommandIcons/RefillCoolant.generated.png",
    "QuickDisembark": "CommandIcons/QuickDisembark.generated.png",
}


def prepare_icon(name: str, source_name: str) -> None:
    # EN: Every icon, including its arrow, comes from ImageGen; processing does not draw replacement shapes.
    # ZH: 每个图标及其箭头均来自 ImageGen；本处理流程不绘制替代图形。
    with Image.open(ROOT / "ArtSource" / source_name) as image:
        original = image.convert("RGBA")

    # EN: Clear negligible generation haze so the intended silhouette fills the small gizmo.
    # ZH: 清除生成图中微弱的外围雾边，使主体轮廓充分利用小尺寸指令图标。
    alpha = original.getchannel("A").point(lambda value: 0 if value < 16 else value)
    original.putalpha(alpha)
    bounds = alpha.getbbox()
    if bounds is None:
        raise ValueError(f"Empty command-icon source / 指令图标原画为空: {source_name}")
    artwork = original.crop(bounds)
    artwork.thumbnail((120, 120), Image.Resampling.LANCZOS)

    icon = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    icon.alpha_composite(artwork, ((128 - artwork.width) // 2, (128 - artwork.height) // 2))
    icon.info.clear()
    path = OUTPUT / f"{name}.png"
    icon.save(path, optimize=True)
    print(f"Mod/Textures/FAF/UI/Commands/{name}.png: 128x128 RGBA, {path.stat().st_size} bytes")


def main() -> None:
    # EN: A scoped export preserves mature icons when adding one new action.
    # ZH: 按范围导出，可在新增单个动作时保留既有成熟图标。
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", choices=tuple(SOURCES), help="Export one icon only / 仅导出指定图标")
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, source in SOURCES.items():
        if args.only is None or name == args.only:
            prepare_icon(name, source)


if __name__ == "__main__":
    main()
