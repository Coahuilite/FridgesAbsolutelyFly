#requires -Version 7.0
# SPDX-License-Identifier: MPL-2.0
<#
EN: Prepare layered cabinet art from manifest-selected self-generated originals.
ZH: 从清单选入的自生成原画制作分层柜体贴图。
EN: Cabinet export delegates to the canonical long-hinge tool; accepted mattress processing stays unchanged.
ZH: 柜体导出委派给权威长边门轴工具；已接受床垫处理保持不变。
#>
[CmdletBinding()]
param(
    # EN: Supply Python 3 with Pillow for cabinet delegation and About artwork.
    # ZH: 提供已安装 Pillow 的 Python 3 解释器，用于柜体委派与 About 美术。
    [string]$PythonExecutable = 'python',
    # EN: Rebuild game textures without rewriting the already accepted About artwork.
    # ZH: 仅重建游戏贴图，不重写已经通过验收的 About 美术。
    [switch]$SkipAbout,
    # EN: Rebuild only mattress PNGs, leaving cabinet and About artwork untouched.
    # ZH: 仅重建床垫 PNG，保持柜体与 About 美术不动。
    [switch]$MattressOnly,
    # EN: Rebuild only cabinet layers/UI, preserving accepted mattress and About files.
    # ZH: 仅重建柜体分层与建造图标，保留已验收床垫及 About 文件。
    [switch]$CabinetOnly
)

$ErrorActionPreference = 'Stop'
if ($MattressOnly -and $CabinetOnly) { throw 'Choose one asset scope. / 请选择一种资产范围。' }
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot 'Mod'
$pixelFormat = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$window = [System.Drawing.Rectangle]::new(31, 51, 194, 410)

function New-Canvas([int]$Width = 256, [int]$Height = 512) {
    return [System.Drawing.Bitmap]::new($Width, $Height, $pixelFormat)
}

function New-ArtGraphics([System.Drawing.Bitmap]$Bitmap) {
    $graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return $graphics
}

function Get-AlphaBounds([System.Drawing.Bitmap]$Bitmap) {
    # EN: Keep the antialiased silhouette while removing external padding.
    # ZH: 去掉外部留白，同时保留抗锯齿轮廓。
    $left = $Bitmap.Width; $top = $Bitmap.Height; $right = -1; $bottom = -1
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -gt 0) {
                if ($x -lt $left) { $left = $x }; if ($x -gt $right) { $right = $x }
                if ($y -lt $top) { $top = $y }; if ($y -gt $bottom) { $bottom = $y }
            }
        }
    }
    if ($right -lt $left) { throw 'Source artwork is empty. / 原始画稿为空。' }
    return [System.Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
}

function Export-Directions(
    [System.Drawing.Bitmap]$Source,
    [string]$Stem,
    [System.Drawing.RotateFlipType]$SourceToNorth = [System.Drawing.RotateFlipType]::RotateNoneFlipNone
) {
    # EN: Only the flat mattress uses planar rotations; cabinet artwork uses fixed-camera views below.
    # ZH: 只有平面床垫使用平面旋转；柜体使用下方的固定镜头方向图。
    $rotations = [ordered]@{
        north = [System.Drawing.RotateFlipType]::RotateNoneFlipNone
        east = [System.Drawing.RotateFlipType]::Rotate90FlipNone
        south = [System.Drawing.RotateFlipType]::Rotate180FlipNone
        west = [System.Drawing.RotateFlipType]::Rotate270FlipNone
    }
    foreach ($direction in $rotations.Keys) {
        $image = [System.Drawing.Bitmap]$Source.Clone()
        try {
            $image.RotateFlip($SourceToNorth)
            $image.RotateFlip($rotations[$direction])
            $destination = Join-Path $runtimeRoot ("{0}_{1}.png" -f $Stem, $direction)
            [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            $image.Save($destination, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Output ("{0}_{1}.png : {2}x{3} RGBA" -f $Stem, $direction, $image.Width, $image.Height)
        }
        finally { $image.Dispose() }
    }
}

function Remove-SourceFringe([System.Drawing.Bitmap]$Bitmap) {
    # EN: Remove low-alpha exterior generation haze; resizing then antialiases the clean silhouette.
    # ZH: 清除生成画稿外围的低透明度雾边，再由缩放为干净轮廓生成抗锯齿边缘。
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            $pixel = $Bitmap.GetPixel($x, $y)
            $alpha = if ($pixel.A -ge 128) { 255 } else { 0 }
            $Bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($alpha, $pixel.R, $pixel.G, $pixel.B))
        }
    }
}

# EN: One canonical exporter keeps ordinary rebuilds on the latest long-side hinge family.
# ZH: 统一权威导出器，确保普通重建使用最新长边门轴家族。
if (-not $MattressOnly) {
    & $PythonExecutable (Join-Path $PSScriptRoot 'Prepare-CabinetUpgrades.py') --include-base
    if ($LASTEXITCODE -ne 0) { throw 'Cabinet artwork preparation failed. / 柜体美术制作失败。' }
}

if (-not $CabinetOnly) {
    $mattressSource = [System.Drawing.Bitmap]::new((Join-Path $projectRoot 'ArtSource/PortableSleepingPad.generated.png'))
    $mattress = New-Canvas
    $graphics = New-ArtGraphics $mattress
    try {
        # EN: Fit the generated silhouette to the visible window; do not bake a rectangle beneath rounded corners.
        # ZH: 将生成轮廓适配可视窗口；不得在圆角下方烘焙不透明矩形底色。
        Remove-SourceFringe $mattressSource
        $bounds = Get-AlphaBounds $mattressSource
        $graphics.DrawImage($mattressSource, $window, $bounds, [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally { $graphics.Dispose(); $mattressSource.Dispose() }
}

try {
    if (-not $CabinetOnly) {
        # EN: BedUtility.GetSlotPos(head:true) puts a north bed's head at minZ (screen bottom).
        # ZH: BedUtility.GetSlotPos(head:true) 将北向床的床头置于 minZ（画面下方）。
        # EN: The pillow-up source is therefore south: N=180, E=270, S=0, W=90 degrees clockwise.
        # ZH: 所以枕头朝上的源图对应南向：北180、东270、南0、西90度顺时针旋转。
        Export-Directions $mattress 'Textures/FAF/Things/PortableSleepingPad' ([System.Drawing.RotateFlipType]::Rotate180FlipNone)
    }
}
finally {
    if (-not $CabinetOnly) { $mattress.Dispose() }
}

# EN: About artwork has its own reproducible raster scaling and palette preparation.
# ZH: About 美术使用独立且可复现的光栅缩放和调色板制作流程。
if (-not $SkipAbout -and -not $MattressOnly -and -not $CabinetOnly) {
    & $PythonExecutable (Join-Path $PSScriptRoot 'Prepare-About.py')
    if ($LASTEXITCODE -ne 0) { throw 'About artwork preparation failed. / About 美术制作失败。' }
}
