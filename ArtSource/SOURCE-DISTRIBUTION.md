# Selected artwork sources / 选定美术来源

[Asset maintenance](../Docs/ASSETS.md) contains attribution, source relationships, geometry and rebuild commands.

[美术维护](../Docs/ASSETS.md)统一维护署名、来源关系、几何约束与重建命令。

- `distribution-manifest.json` selects Git-maintained inputs through `ownedFiles` and records exporter inputs in `tools`／`toolInputs`.
- `distribution-manifest.json`以`ownedFiles`选择Git维护输入，以`tools`／`toolInputs`记录导出依赖。
- `distribution-provenance.json` records production relationships; `distribution-metadata.json` records image format metadata.
- `distribution-provenance.json`记录制作关系；`distribution-metadata.json`记录图像格式信息。

Selected ArtSource is repository material, not player-package content. External references are excluded from both.

选定ArtSource只用于仓库维护，不随玩家包；外部参考不进入仓库或玩家包。
