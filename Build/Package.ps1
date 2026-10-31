#requires -Version 7.0
# SPDX-License-Identifier: MPL-2.0
# EN: Package only Mod runtime and player instructions; sources, tools and evidence stay local.
# 中：只打包 Mod 运行内容与玩家说明；原稿、源码、工具及工程证据留在本地。
param(
    [string]$ReleaseTag = '',
    [switch]$ListInputs,
    [ValidateSet('Dev','Steam','GitHub')][string]$Channel = 'GitHub'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime = [IO.Path]::GetFullPath((Join-Path $root 'Mod'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $root 'Release'))
$stageParent = $releaseRoot
if (-not [string]::IsNullOrEmpty($ReleaseTag)) {
    if ($ReleaseTag -notmatch '\A(?!(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z)[A-Za-z0-9][A-Za-z0-9_-]*\z') {
        throw 'ReleaseTag must be one safe folder name: letters, digits, hyphen or underscore; no device names.'
    }
    $stageParent = [IO.Path]::GetFullPath((Join-Path $releaseRoot $ReleaseTag))
}
$channelName = $Channel.ToLowerInvariant()
if ($channelName -ne 'github') { $stageParent = Join-Path $stageParent $channelName }
$packageFolder = 'FAF_FridgesAbsolutelyFly'
$stage = [IO.Path]::GetFullPath((Join-Path $stageParent $packageFolder))
$zip = Join-Path $stageParent ($packageFolder + '.zip')
if (-not $stage.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Staging target must stay inside the project Release directory.'
}
foreach ($path in @($root, $runtime, $releaseRoot, $stageParent, (Split-Path -Parent $stageParent))) {
    if ((Test-Path -LiteralPath $path) -and
            ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Package roots must not be directory links.'
    }
}
# EN: Never clean or overwrite older releases, including the historical untagged stage.
# 中：不清理或覆盖旧发行，包括历史无标签暂存目录。
if (-not $ListInputs -and ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip))) {
    throw 'Release output already exists; select a new ReleaseTag instead of replacing it.'
}
$inputs = [Collections.Generic.List[object]]::new()
function Add-RuntimeInput([string]$Path) {
    $relative = [IO.Path]::GetRelativePath($runtime, $Path)
    $inputs.Add([pscustomobject]@{ Source = $Path; Relative = $relative; Content = $null })
}
# EN: All channels share product version and binaries; only this generated identity differs.
# 中：所有渠道共用产品版本与二进制，仅生成的渠道身份不同。
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'Source/FAF/FAF.csproj') -Raw
[xml]$about = Get-Content -LiteralPath (Join-Path $runtime 'About/About.xml') -Raw
$productVersion = [string]$project.Project.PropertyGroup.Version
if ($productVersion -notmatch '^\d+\.\d+\.\d+$' -or $about.ModMetaData.modVersion -cne $productVersion) {
    throw 'csproj Version and About modVersion must contain the same product version.'
}
# EN: Formal player artifacts originate on main; Dev also accepts the matching minor branch.
# 中：正式玩家产物须来自main；Dev另允许对应minor开发分支。
$branch = @(& git -C $root symbolic-ref --quiet --short HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $branch.Count -ne 1) { throw 'Package from main or the matching named development branch, not detached HEAD.' }
$version = [version]$productVersion
$developmentBranch = "$($version.Major).$($version.Minor).x"
if ($branch[0] -cne 'main' -and ($channelName -ne 'dev' -or $branch[0] -cne $developmentBranch)) {
    throw "Steam/GitHub packages require main; Dev may also use $developmentBranch."
}
$commit = @(& git -C $root rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or $commit.Count -ne 1) { throw 'Cannot read package source commit.' }
$commit = [string]$commit[0]
$changes = @(& git -C $root status --porcelain --untracked-files=normal 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read package source state.' }
$dirty = $changes.Count -gt 0
if ($channelName -ne 'dev' -and $dirty) { throw 'Steam/GitHub packages require clean committed source; use Dev for a local trial.' }
$displayVersion = $productVersion
if ($channelName -eq 'dev') {
    $displayVersion += '-dev.' + $commit.Substring(0, 8)
    if ($dirty) { $displayVersion += '.dirty' }
}
$identity = [ordered]@{
    schemaVersion = 1
    version = $productVersion
    channel = $channelName
    sourceCommit = $commit
    dirty = $dirty
    displayVersion = $displayVersion
}
$inputs.Add([pscustomobject]@{
    Source = $null
    Relative = 'About/BuildInfo.json'
    Content = [Text.UTF8Encoding]::new($false).GetBytes(($identity | ConvertTo-Json) + "`n")
})
# EN: Ship the unmodified license and a source notice derived from the same build identity.
# 中：附带未改许可证，并从同一构建身份生成源码取得说明。
$licensePath = Join-Path $root 'LICENSE'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw 'Missing root MPL-2.0 LICENSE.' }
$inputs.Add([pscustomobject]@{ Source = $licensePath; Relative = 'LICENSE'; Content = $null })
$sourceUrl = "https://github.com/Coahuilite/FridgesAbsolutelyFly/tree/$commit"
$sourceLocation = if ($dirty) {
    "Base commit only / 仅源码基点：$sourceUrl`n`nThis is a locally modified development build. The base URL is not the complete source used by this build. Before distributing it, the distributor must also make the corresponding modified MPL-covered source available to recipients.`n这是包含本地未提交修改的开发构建；基点链接不是本构建的完整源码。分发前，分发者还须向接收者提供本构建实际修改后的MPL覆盖源码。"
} else {
    "Exact source for this build / 本构建对应源码：$sourceUrl"
}
$sourceNotice = @"
# Source code / 源码

F.A.F. source code is available under the Mozilla Public License 2.0 (MPL-2.0). See the complete LICENSE included beside this notice.
F.A.F.源码按Mozilla Public License 2.0（MPL-2.0）提供，完整文本见本说明旁的LICENSE。

Version / 版本：$($identity.version)
Channel / 渠道：$($identity.channel)
Source commit / 源码提交：$($identity.sourceCommit)
Dirty / 本地未提交修改：$($identity.dirty.ToString().ToLowerInvariant())

$sourceLocation

For visual-asset and dependency rights, see THIRD_PARTY_NOTICES.md.
视觉资产与依赖权利范围见THIRD_PARTY_NOTICES.md。
"@
$inputs.Add([pscustomobject]@{
    Source = $null
    Relative = 'SOURCE_NOTICE.md'
    Content = [Text.UTF8Encoding]::new($false).GetBytes($sourceNotice + "`n")
})
foreach ($name in 'About/About.xml','About/Preview.png','About/ModIcon.png',
        'LoadFolders.xml','README.md','THIRD_PARTY_NOTICES.md') {
    $path = Join-Path $runtime $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing runtime input: $name" }
    Add-RuntimeInput $path
}
$chinesePreview = Join-Path $runtime 'About/Preview_zh-Hans.png'
if (Test-Path -LiteralPath $chinesePreview -PathType Leaf) { Add-RuntimeInput $chinesePreview }
foreach ($directory in 'Defs','Languages','Textures') {
    $path = Join-Path $runtime $directory
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Missing runtime directory: $directory" }
    foreach ($item in Get-ChildItem -LiteralPath $path -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Runtime inputs must not contain links.' }
        if ($item.PSIsContainer) { continue }
        $expectedExtension = if ($directory -eq 'Textures') { '.png' } else { '.xml' }
        if ($item.Extension -ne $expectedExtension) { throw "Unexpected runtime file type in $directory." }
        Add-RuntimeInput $item.FullName
    }
}
foreach ($version in '1.6','1.7') {
    $path = Join-Path $runtime "$version/Assemblies/FAF.FridgesAbsolutelyFly.dll"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Build $version before packaging." }
    Add-RuntimeInput $path
}
$destinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $inputs) {
    if ($entry.Source) {
        for ($ancestor = $entry.Source; ; $ancestor = Split-Path -Parent $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Package inputs may not traverse links.'
            }
            if ($ancestor.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { break }
        }
    }
    $destination = [IO.Path]::GetFullPath((Join-Path $stage $entry.Relative))
    if (-not $destination.StartsWith($stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Input escapes the package root.'
    }
    if (-not $destinations.Add($destination)) { throw 'Duplicate package destination.' }
}
# EN: Verification consumes this same whitelist without creating a stage or ZIP.
# 中：核验复用同一白名单，不创建暂存目录或ZIP。
if ($ListInputs) { return $inputs.ToArray() }
if (-not (Test-Path -LiteralPath $stageParent)) { New-Item -ItemType Directory -Path $stageParent | Out-Null }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($entry in $inputs) {
    $destination = Join-Path $stage $entry.Relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    if ($entry.Source) { Copy-Item -LiteralPath $entry.Source -Destination $destination }
    else { [IO.File]::WriteAllBytes($destination, $entry.Content) }
}
if ($channelName -eq 'github') {
    Compress-Archive -LiteralPath $stage -DestinationPath $zip
    Write-Output "Package: $zip"
}
Write-Output "Stage ($channelName / $displayVersion): $stage"
