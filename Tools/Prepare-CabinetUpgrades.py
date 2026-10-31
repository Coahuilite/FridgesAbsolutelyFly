# SPDX-License-Identifier: MPL-2.0
"""Export long-hinged cabinet families. / 导出长边铰接冷柜家族。"""

import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps


ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "ArtSource" / "UpgradeStudies"
RUNTIME = ROOT / "Mod" / "Textures" / "FAF"
CANVAS = (512, 512)
NS_SOURCE_WINDOW = (195, 210, 494, 1137)
EW_SOURCE_WINDOW = (223, 257, 1172, 328)
NS_WINDOW = (207.5, 127.9, 97, 205)
EW_WINDOW = (153.5, 181.9, 205, 97)
STAGES = {
    "Base": {
        "vertical": ART / "Base-vertical-long-hinge.corrected.png",
        "east": ART.parent / "FlyingCabinetHorizontal.generated.png",
        "west_window": (223, 243, 1172, 328),
    },
    "Pepper": {
        "vertical": ART / "Pepper-vertical-long-hinge.corrected.png",
        "east": ART / "Pepper-horizontal.generated.png",
        "west_window": EW_SOURCE_WINDOW,
    },
    "Salt": {
        "vertical": ART / "Salt-vertical-long-hinge.corrected.png",
        "east": ART / "Salt-horizontal.generated.png",
        "west_window": EW_SOURCE_WINDOW,
    },
}


def read_source(path: Path) -> Image.Image:
    with Image.open(path) as source:
        image = source.convert("RGBA")
    # EN: Match the established silhouette cleanup; new PNG canvases carry no source metadata.
    # ZH: 沿用既有轮廓清理；新 PNG 画布不携带源元数据。
    image.putalpha(image.getchannel("A").point(lambda value: 255 if value >= 128 else 0))
    return image


def mapped(image: Image.Image, scale_x: float, scale_y: float, offset_x: float, offset_y: float) -> Image.Image:
    return image.transform(
        CANVAS,
        Image.Transform.AFFINE,
        (1 / scale_x, 0, -offset_x / scale_x, 0, 1 / scale_y, -offset_y / scale_y),
        Image.Resampling.BICUBIC,
    )


def project_vertical(image: Image.Image) -> Image.Image:
    # EN: One complete-master mapping preserves the full front wall, without nine-slice compression.
    # ZH: 一次完整母图映射保留全部前壁，不做九宫挤压。
    x, y, width, height = NS_SOURCE_WINDOW
    left, top, view_width, view_height = NS_WINDOW
    sx, sy = view_width / width, view_height / height
    return mapped(image, sx, sy, left - x * sx, top - y * sy)


def project_horizontal(image: Image.Image, preserve_upgrade_hardware: bool, source_window: tuple = EW_SOURCE_WINDOW) -> Image.Image:
    x, y, width, height = source_window
    left, top, view_width, view_height = EW_WINDOW
    sx = view_width / width
    if preserve_upgrade_hardware:
        # EN: Keep rear engine/nozzle height; the base's fixed fourteen-pixel lip would squash added pods.
        # ZH: 保留后部发动机/喷口高度；基柜固定十四像素后沿会压扁新增舱体。
        rear_scale = sx
    else:
        rear_scale = 14 / (y - image.getchannel("A").getbbox()[1])
    result = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    bands = [
        (0, math.ceil(top), rear_scale, top - y * rear_scale),
        (math.ceil(top), math.ceil(top + view_height), view_height / height, top - y * view_height / height),
        (math.ceil(top + view_height), 512, sx, top + view_height - (y + height) * sx),
    ]
    # EN: Only the roof needs a separate projection; the complete front face scales uniformly with width.
    # ZH: 仅顶面单独校正投影；完整前壁与宽度等比缩放。
    for first_row, last_row, sy, offset_y in bands:
        layer = mapped(image, sx, sy, left - x * sx, offset_y)
        result.paste(layer.crop((0, first_row, 512, last_row)), (0, first_row))
    return result


def opposite_vertical_facing(image: Image.Image) -> Image.Image:
    # EN: N/S exchange left/right long hinge sides while the front wall stays screen-bottom.
    # ZH: 南北互换左右长边门轴，前壁仍保持屏幕下方。
    registered = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    registered.alpha_composite(ImageOps.mirror(image), (1, 0))
    return registered


def frame_from(interior: Image.Image, window: tuple) -> Image.Image:
    frame = interior.copy()
    x, y, width, height = window
    # EN: Clear the full registered opening so native contents are never covered by an opaque glass pane.
    # ZH: 清空完整登记窗口，避免不透明玻璃覆盖原生内容物。
    ImageDraw.Draw(frame).rectangle(
        (math.ceil(x), math.ceil(y), math.ceil(x + width) - 1, math.ceil(y + height) - 1),
        fill=(0, 0, 0, 0),
    )
    return frame


def save_png(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.info.clear()
    image.save(path, optimize=True)
    print(f"{path.relative_to(ROOT).as_posix()}: {image.width}x{image.height} RGBA, {path.stat().st_size} bytes")


def export_stage(stage: str, horizontal_only: bool = False) -> None:
    spec = STAGES[stage]
    vertical = project_vertical(read_source(spec["vertical"]))
    east = project_horizontal(read_source(spec["east"]), stage != "Base")
    # EN: West is independently generated; flipping the full H image would put its front wall on top.
    # ZH: 西向独立生成；翻转整张横图会把前壁移到上方。
    west = project_horizontal(
        read_source(ART / f"{stage}-horizontal-long-hinge-west.corrected.png"),
        stage != "Base", spec["west_window"],
    )
    directions = ("east", "west") if horizontal_only else ("north", "east", "south", "west")
    for direction in directions:
        if direction in ("north", "south"):
            interior = opposite_vertical_facing(vertical) if direction == "south" else vertical.copy()
            window = NS_WINDOW
        else:
            interior = (west if direction == "west" else east).copy()
            window = EW_WINDOW
        frame = frame_from(interior, window)
        frame_stem = "FlyingCabinet" if stage == "Base" else f"FlyingCabinet{stage}Frame"
        interior_stem = "FlyingCabinetInterior" if stage == "Base" else f"FlyingCabinet{stage}Interior"
        save_png(frame, RUNTIME / "Buildings" / f"{frame_stem}_{direction}.png")
        save_png(interior, RUNTIME / "Buildings" / f"{interior_stem}_{direction}.png")
    # EN: Crop only transparent drawing-pad margins for the stage-specific UI image.
    # ZH: 阶段 UI 图只裁去绘制留白，不裁去柜体。
    if not horizontal_only:
        ui_name = "FlyingCabinet" if stage == "Base" else f"FlyingCabinet{stage}"
        save_png(vertical.crop(vertical.getchannel("A").getbbox()), RUNTIME / "UI" / f"{ui_name}.png")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stages", nargs="+", choices=("Pepper", "Salt"), default=["Pepper", "Salt"])
    parser.add_argument("--include-base", action="store_true", help="Also publish all current base directions/UI / 同时发布基柜全部方向及UI")
    parser.add_argument("--base-door-fix", action="store_true", help="Legacy scope: base E/W only, now using long-hinge sources / 旧范围：仅基柜东西图，现使用长边门轴源")
    args = parser.parse_args()
    for stage in args.stages:
        export_stage(stage)
    if args.include_base:
        export_stage("Base")
    elif args.base_door_fix:
        export_stage("Base", horizontal_only=True)


if __name__ == "__main__":
    main()
