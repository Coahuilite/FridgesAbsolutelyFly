# F.A.F. asset maintenance / F.A.F. 资产维护

Visual assets: GPT Images 2.5 (OpenAI)

视觉资产：GPT Images 2.5（OpenAI）

## Sources and repository boundary / 来源与仓库边界

The freezer family shares `ArtSource/FlyingCabinet.master.png`: white-grey housing, indigo compressor panels and cyan controls. PEPPER adds compact nozzles; SALT adds close-fitting shuttle hardware. The sleeping pad has its own textile design parent.

冷柜家族共用`ArtSource/FlyingCabinet.master.png`：白灰机壳、深靛压缩机嵌板及青色控制区。PEPPER增加小型喷口，SALT增加贴体太空梭设备。睡垫另有独立织物设计父源。

[The source manifest](../ArtSource/distribution-manifest.json) selects 34 Git-maintained ArtSource files: 30 generated images/production parents and four source documents. Its `ownedFiles` list is the selection authority; `tools` and `toolInputs` describe the five exporters and their required inputs. [Provenance](../ArtSource/distribution-provenance.json) records source relationships; [format metadata](../ArtSource/distribution-metadata.json) records image dimensions and formats.

[来源清单](../ArtSource/distribution-manifest.json)选入34项Git维护文件：30张生成图/制作父源及四份来源文档。`ownedFiles`为选入依据，`tools`与`toolInputs`记录五个导出器及必要输入。[来源关系](../ArtSource/distribution-provenance.json)和[格式元数据](../ArtSource/distribution-metadata.json)分别记录图像脉络、尺寸与格式。

`Source`, `Tools` and selected `ArtSource` are development-repository material, not player-package contents. External references, including the original Nira portrait, other mods' artwork and native-game icons, are excluded from the public repository and player package. Raster exports use the selected self-generated sources without external reference files.

`Source`、`Tools`及选定`ArtSource`用于开发仓库，不进入玩家包。外部参考，包括Nira原立绘、其他模组原图及原版图标，不进入公开仓库或玩家包。光栅导出只读取选定自生成源，无需外部参考原图。

| Asset / 资产 | Current source relationship / 当前来源关系 |
|---|---|
| Cabinet design / 柜体设计 | `FlyingCabinet.master.png` → `UpgradeStudies/{Pepper-retrofit,Salt-shuttle}.master.png` |
| N/S and UI / 南北及UI | `UpgradeStudies/{Base,Pepper,Salt}-vertical-long-hinge.corrected.png`; South mirrors North left/right / 南向由北向左右镜像 |
| East / 东向 | Base: `FlyingCabinetHorizontal.generated.png`; upgrades: `UpgradeStudies/{Pepper,Salt}-horizontal.generated.png` |
| West / 西向 | `UpgradeStudies/{Base,Pepper,Salt}-horizontal-long-hinge-west.corrected.png` |
| Unfolded pad / 铺开睡垫 | `PortableSleepingPad.original.png` → `PortableSleepingPad.generated.png`; the latter is the export input / 后者为导出输入 |
| Packed pad / 卷垫 | `PortableSleepingPad.original.png` → `PortableSleepingPadPacked.generated.png` |
| Commands / 指令 | `Command{Fly,InstallSleepingPad,LaunchCabinet}.generated.png`, plus `CommandIcons/{NearFieldPulse,RefillCoolant,QuickDisembark}.generated.png` |
| ModIcon / 模组图标 | Freezer-only crop from `CommandFly.generated.png` → `ModIcon.derived.png` / 仅裁出单柜 |
| English cover / 英文封面 | `IndependentCover/{stage-A-new-pose,stage-B-color-block-character,stage-C-generated-cover}.png` → `IndependentCover/IndependentCover.1280x720.png` → `Preview.generated.png` |
| Chinese cover / 中文封面 | `IndependentCover/IndependentCover.1280x720.png` → `IndependentCover/Chinese/ChineseCover{.generated,.1280x720}.png` / 英文最终图的文字本地化派生 |

All source paths in this table are relative to `ArtSource/`. The textile parent also supplies the cabinet-pad identity in the install icon and cover. Command/cover equipment follows the same freezer design family; detailed input roles are in the provenance JSON.

表中源路径均相对`ArtSource/`。织物父源也为安装指令和封面提供睡垫身份；指令与封面设备沿用同一冷柜家族，具体输入角色见来源JSON。

## Per-asset metadata / 逐资产元数据

[Provenance](../ArtSource/distribution-provenance.json) indexes selected source images and every runtime PNG; [format metadata](../ArtSource/distribution-metadata.json) records their dimensions, colour mode and alpha information. Records live in companion JSON, not text chunks inside game PNGs.

[来源目录](../ArtSource/distribution-provenance.json)逐项记录选定源图和所有运行PNG，[格式目录](../ArtSource/distribution-metadata.json)记录尺寸、颜色模式及alpha信息。元数据用伴随JSON保存，不写入游戏PNG的文本块。

Creation records distinguish generation without references, generation with references, image editing, programmatic derivation and direct copies. Reference roles distinguish the editable base from character, style and layout references; parent links preserve the image family.

制作记录区分无参考生成、带参考生成、图像编辑、程序派生及直接副本。参考用途区分编辑底图与角色、风格、构图参考，父源链接保留资产家族关系。

Recovered prompts retain the actual generation instructions; privacy redactions and normalization to current source-filename aliases are identified in the prompt metadata. Missing prompts stay unknown; reconstructed descriptions or inferred processing recipes are labelled accordingly. Exported or copied images point to their parents rather than claiming a new generation prompt. Unselected historical records remain local.

恢复的提示词保留实际生成指令，隐私脱敏及源文件名改为现行别名的处理在提示词元数据中标明；缺失提示词保持未知，重构说明或推断配方明确标注。导出和复制图沿父源追溯，不伪造新的生成提示词；未选入的历史记录仅在本地保留。

The GPT Images 2.5 attribution follows maintainer confirmation. Per-call evidence remains separate: a record that identifies only the gpt-image family is not promoted to an observed API model ID.

GPT Images 2.5署名依据维护者确认；逐调用证据另行记录，只能证明gpt-image家族的记录不被写成已观测到具体API模型ID。

Usage permission and third-party rights are defined in [the canonical notices](../Mod/THIRD_PARTY_NOTICES.md). Reference metadata records evidence and its limits; it does not grant rights to reference originals or characters.

使用许可及第三方权利范围见[统一声明](../Mod/THIRD_PARTY_NOTICES.md)。参考元数据记录依据及其限度，不授予参考原图或角色权利。

## Runtime outputs / 运行输出

There are 41 PNGs. Texture paths below are relative to the project root; game `texPath` values omit `Mod/Textures/`, `.png` and Multi direction suffixes. Directions are `north`, `east`, `south`, `west`.

运行输出共41张PNG。下表路径相对项目根；游戏`texPath`去掉`Mod/Textures/`、`.png`及Multi方向后缀。四向为`north`、`east`、`south`、`west`。

| Use / 用途 | Files / 文件 | Count / 数量 | Format / 格式 |
|---|---|---:|---|
| Base layers / 基柜分层 | `Mod/Textures/FAF/Buildings/FlyingCabinet{,Interior}_{direction}.png` | 8 | 512×512 RGBA |
| Upgrade layers / 升级分层 | `Mod/Textures/FAF/Buildings/FlyingCabinet{Pepper,Salt}{Frame,Interior}_{direction}.png` | 16 | 512×512 RGBA |
| Build/copy/packed UI / 建造、复制及打包UI | `Mod/Textures/FAF/UI/FlyingCabinet{,Pepper,Salt}.png` | 3 | Base 134×288; PEPPER 153×291; SALT 164×297 RGBA |
| Unfolded pad / 铺开睡垫 | `Mod/Textures/FAF/Things/PortableSleepingPad_{direction}.png` | 4 | N/S 256×512; E/W 512×256 RGBA |
| Packed pad / 卷垫 | `Mod/Textures/FAF/Things/PortableSleepingPadPacked.png` | 1 | 256×256 RGBA |
| Commands / 指令 | `Mod/Textures/FAF/UI/Commands/{Fly,InstallSleepingPad,LaunchCabinet,NearFieldPulse,RefillCoolant,QuickDisembark}.png` | 6 | 128×128 RGBA |
| Mod icon / 模组图标 | `Mod/About/ModIcon.png` | 1 | 64×64 RGBA |
| English preview / 英文封面 | `Mod/About/Preview.png` | 1 | 1280×720 RGB; 769390 bytes |
| Chinese alternate / 中文附加封面 | `Mod/About/Preview_zh-Hans.png` | 1 | 1280×720 RGB; 781171 bytes |

The Chinese preview is an optional alternate file, not automatic language-based switching. Both covers are under 1 MB. The English source alias is copied unchanged; final cover resizing uses whole-image LANCZOS and seven-bit-per-channel colour reduction, without cropping or compositing.

中文封面为可选附加文件，不实现自动按语言切换。两版封面均小于1MB。英文源别名原样复制；封面最终尺寸处理采用全图LANCZOS缩放及每通道七位色彩缩减，不裁切或合成。

## Cabinet geometry and layers / 柜体几何与分层

Logical footprint is **1×2**, while each 512² frame/interior uses **4×4 Graphic_Multi drawSize**, a shared origin and **zero drawOffset**. The transparent pad accommodates the full front wall rather than compressing it into the floor footprint. No custom shader or AssetBundle is required.

逻辑占地为**1×2**；每张512方形柜框/内衬使用**4×4 Graphic_Multi drawSize**、共用原点和**零drawOffset**。透明留白容纳完整前壁，不把立体机身压入地面占地；无需自定义shader或AssetBundle。

| Opening / 开口 | Pixel rectangle (x,y,width,height) / 像素矩形 |
|---|---|
| N/S / 南北 | `(207.5,127.9,97,205)` |
| E/W / 东西 | `(153.5,181.9,205,97)` |
| Common centre / 共用中心 | `(256,230.4)` = world / 世界 `(x,z)=(0,+0.20)` |

Layer order is **interior/body → sleeping pad and real contents → foreground frame**. The frame opening has alpha 0; glass reflections belong to the lower lining image. The renderer applies the fixed +z0.20 top offset to bedding/contents/sleepers without rotating it with logical Rot4 facing. The final flight/rotation transform then applies to cabinet and contents together. Native pawn size is preserved.

分层顺序为**内衬/机身→睡垫及真实内容物→前景柜框**。前框开口alpha为0，玻璃反射放在下层内衬。渲染器对床品/物品/睡眠者施加固定+z0.20顶面偏移，不随逻辑Rot4横转该偏移；随后柜体与内容物共同接受飞行/旋转变换。pawn保持原生尺寸。

N/S use a complete vertical-source mapping; South mirrors North horizontally. E/W use separate fixed-camera sources with their front walls always screen-bottom, never a 90° planar rotation or vertical flip of the cabinet. Horizontal roof alignment is separate from the full-width front-wall scaling. Projection source anchors are in `Tools/Prepare-CabinetUpgrades.py` and the provenance JSON.

南北使用完整纵图映射，南向左右镜像北向。东西使用独立固定镜头源，前壁始终朝屏幕下方，不将整柜平转90°或上下翻转；横图屋顶对齐与完整前壁的等宽缩放分开处理。源投影锚点见`Tools/Prepare-CabinetUpgrades.py`及来源JSON。

| Facing / 朝向 | Lid hinge / 顶盖铰链 | Latch / 锁扣 | Base solid bounds / 基柜实体像素边界 |
|---|---|---|---|
| North / 北 | Left long edge / 左长边 | Right / 右 | `(189,107)..(323,394)` |
| East / 东 | Upper long edge / 上长边 | Lower / 下 | `(133,168)..(379,332)` |
| South / 南 | Right long edge / 右长边 | Left / 左 | `(190,107)..(324,394)` |
| West / 西 | Lower long edge / 下长边 | Upper / 上 | `(133,168)..(379,333)` |

At 128 pixels/world unit, the front region from opening to base is approximately 0.48 world units in N/S and 0.41 in E/W. Short-end handles are carrying/service hardware, not another lid axis; cyan strips are controls.

按每世界单位128像素，开口到底座的前部区域南北约0.48世界单位、东西约0.41。短端把手为搬运/检修结构，不是另一门轴；青色条是控制板。

## Pad directions and alpha / 睡垫方向与透明度

The pillow-up source is South. Clockwise export rotations are **N=180°, E=270°, S=0°, W=90°**, giving pillow positions **N/bottom, E/left, S/top, W/right**. The unfolded pad uses a normal (1,2) drawing plane. Its visible bounds are `(31,51)..(225,461)` in N/S and `(51,31)..(461,225)` in E/W.

枕头朝上的源图对应南向。顺时针导出角度为**北180°、东270°、南0°、西90°**，枕头位置为**北下、东左、南上、西右**。铺开睡垫使用正常(1,2)绘制平面；可见边界南北为`(31,51)..(225,461)`，东西为`(51,31)..(461,225)`。

Keep rounded-corner exterior pixels transparent and white fabric opaque: bounding-rectangle corners have alpha 0; fabric centre has alpha 255. Export fits the source silhouette to `(31,51,194,410)` without an opaque rectangular underlay. Do not remove white pixels by colour.

圆角外保持透明，白织物保持不透明：可见边界矩形四角alpha为0，织物中心为255。导出将源轮廓适配`(31,51,194,410)`，不在下方绘制不透明矩形；不得按颜色去除白像素。

The packed pad uses a single (1,1) drawing plane. Its export crops transparent margins, scales within 224×224 and centres on 256²; alpha bounds are `(16,71)..(240,184)`. Command exports fit within 120×120 on 128². ModIcon fits within 60×60 on 64² and uses crop `(0,431,1254,1254)` from the Fly source; update this crop if that source composition changes.

卷垫使用单方向(1,1)绘制平面：裁透明边、等比适配224×224，再居中放入256方图；alpha边界为`(16,71)..(240,184)`。指令图适配120×120后放入128方图。ModIcon适配60×60后放入64方图，读取Fly源图的裁切区域`(0,431,1254,1254)`；若源构图改变，应同步该区域。

## Rebuild from selected sources / 从选定源重建

Use **Python 3.9+ with Pillow**, plus **PowerShell 7 and Windows System.Drawing** for the main texture entry. `python` must resolve to that interpreter; `-PythonExecutable` can select it explicitly. These are maintainer dependencies, not game dependencies. Run from the development repository root; tools derive paths from their own locations and do not call image generation.

需**Python3.9以上及Pillow**；主贴图入口另需**PowerShell7及Windows System.Drawing**。`python`应解析为该解释器，也可用`-PythonExecutable`明确指定。这些是维护依赖，不是游戏依赖。从开发仓库根目录运行；工具从自身位置推导路径，不调用图像生成。

~~~powershell
# All 41 runtime PNGs / 全部41张运行PNG
pwsh -File ./Tools/Prepare-Textures.ps1 -PythonExecutable python
python ./Tools/Prepare-CommandIcons.py
python ./Tools/Prepare-PackedMattress.py
Copy-Item -LiteralPath ./ArtSource/IndependentCover/Chinese/ChineseCover.1280x720.png -Destination ./Mod/About/Preview_zh-Hans.png
~~~

For a scoped rebuild / 按范围重建：

~~~powershell
# Cabinet layers/UI only / 仅柜体分层和UI
python ./Tools/Prepare-CabinetUpgrades.py --include-base
# Unfolded pad only / 仅铺开睡垫
pwsh -File ./Tools/Prepare-Textures.ps1 -MattressOnly -PythonExecutable python
# One command / 单个指令
python ./Tools/Prepare-CommandIcons.py --only InstallSleepingPad
# English preview only, keeping ModIcon / 仅英文封面，保留ModIcon
python ./Tools/Prepare-About.py --preview-only
~~~

`Prepare-Textures.ps1 -CabinetOnly` delegates to the cabinet exporter; `-SkipAbout` omits About writes. `-MattressOnly` and `-CabinetOnly` are mutually exclusive. Without scope switches the main entry exports cabinet layers/UI, unfolded pad and English About/ModIcon; the other commands above supply packed pad, six commands and the Chinese alternate. Build/package operations do not automatically run art exporters.

`Prepare-Textures.ps1 -CabinetOnly`委派柜体导出器；`-SkipAbout`跳过About。`-MattressOnly`与`-CabinetOnly`互斥。无范围开关时，主入口导出柜体分层/UI、铺开睡垫及英文About/ModIcon；上方其余命令补齐卷垫、六枚指令及中文附加封面。构建/打包不会自动运行美术导出器。
