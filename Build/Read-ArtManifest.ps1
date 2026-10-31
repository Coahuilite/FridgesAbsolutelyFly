#requires -Version 7.0
# SPDX-License-Identifier: MPL-2.0
# EN: Read-only owned-art manifest validation for maintenance and explicit Git staging, not player packaging.
# 中：自有美术清单只读验证，服务维护与显式Git暂存，不参与玩家打包。
function Read-ArtDistributionManifest {
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $root = [IO.Path]::GetFullPath($ProjectRoot)
    function Resolve-ListedFile([string]$Relative, [string]$Prefix, [string[]]$Extensions) {
        if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.Contains('\') -or [IO.Path]::IsPathRooted($Relative)) {
            throw 'Art manifest paths must be canonical forward-slash relative files.'
        }
        $segments = $Relative.Split('/')
        if ($segments.Count -lt 2 -or $segments[0] -cne $Prefix) { throw 'Art manifest entry is outside its permitted source tree.' }
        foreach ($segment in $segments) {
            if ($segment -notmatch '\A[A-Za-z0-9][A-Za-z0-9._-]*\z' -or $segment.EndsWith('.') -or
                $segment -match '\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)') {
                throw 'Art manifest contains an unsafe path segment.'
            }
        }
        if ([IO.Path]::GetExtension($Relative) -notin $Extensions) { throw 'Art manifest contains an unapproved file format.' }
        $path = [IO.Path]::GetFullPath((Join-Path $root $Relative))
        if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Art manifest entry escapes the project root.'
        }
        # EN: Check every ancestor, not only the file, before reading or copying any listed content.
        # 中：读取或复制前检查文件及全部祖先，不仅检查文件本身。
        for ($ancestor = $path; ; $ancestor = Split-Path -Parent $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Art manifest paths may not traverse links.' }
            if ($ancestor.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { break }
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Art manifest input is not a real file.' }
        return $path
    }
    $manifestRelative = 'ArtSource/distribution-manifest.json'
    $manifestPath = Resolve-ListedFile $manifestRelative 'ArtSource' @('.json')
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.pathBasis -cne 'projectRoot' -or
        @($manifest.ownedFiles).Count -eq 0 -or @($manifest.tools).Count -eq 0) {
        throw 'Unsupported or incomplete artwork distribution manifest.'
    }
    $owned = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $manifest.ownedFiles) {
        $null = Resolve-ListedFile $relative 'ArtSource' @('.png','.json','.md')
        if (-not $owned.Add($relative)) { throw 'Duplicate artwork distribution input.' }
    }
    if (-not $owned.Contains($manifestRelative)) { throw 'The distributed source list must include its own manifest.' }
    $tools = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $manifest.tools) {
        $null = Resolve-ListedFile $relative 'Tools' @('.py','.ps1')
        if (-not $tools.Add($relative)) { throw 'Duplicate artwork tool entry.' }
    }
    foreach ($property in $manifest.toolInputs.PSObject.Properties) {
        if (-not $tools.Contains($property.Name)) { throw 'Artwork input coverage names an unapproved tool.' }
        foreach ($relative in @($property.Value)) {
            if (-not $owned.Contains($relative)) { throw 'An artwork tool depends on an unlisted/external input.' }
        }
    }
    foreach ($tool in $tools) {
        if ($null -eq $manifest.toolInputs.PSObject.Properties[$tool]) { throw 'Artwork tool has no declared input coverage.' }
    }
    return $manifest
}
