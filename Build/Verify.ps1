#requires -Version 7.4
# SPDX-License-Identifier: MPL-2.0
<#
EN: Read-only gate. -FullHistory scans HEAD ancestry; -Archive/-PackageDirectory audit a real channel output.
中：只读门禁。-FullHistory检查HEAD祖先；-Archive/-PackageDirectory核验实际渠道产物。
#>
[CmdletBinding()]
param(
    [switch]$FullHistory,
    [switch]$PrePush,
    [string]$PullRequestSourceBranch,
    [string]$Archive,
    [string]$PackageDirectory,
    [ValidateSet('Dev','Steam','GitHub')][string]$Channel = 'GitHub',
    [string]$NotesFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$failures = [Collections.Generic.List[string]]::new()
$utf8 = [Text.UTF8Encoding]::new($false, $true)
if ($Archive -and $PackageDirectory) { throw 'Choose one archive or one package directory per verification.' }
if ($Archive -and $Channel -ne 'GitHub') { throw 'Only the GitHub channel produces an archive.' }

function Fail([string]$Message) { $script:failures.Add($Message) }
function Check-SourcePath([string]$Path, [string]$Label) {
    if ($Path -match '(^|/)(\.references|\.build|Release|bin|obj|\.vs|\.vscode)/|\.(dll|pdb|log|zip|cache)$|(^|/)PublishedFileId\.txt$|\.local\.(ps1|json)$') {
        Fail "$Label : private/build artifact in source history/selection"
    }
}
function Git-Lines([string[]]$Arguments) {
    $lines = @(& git -C $root @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "Git read failed: $($Arguments[0])" }
    return ,$lines
}
function Check-PushRefs([string]$DevelopmentBranch) {
    # EN: Reuse the working-tree gate only when every real outgoing update resolves to clean HEAD.
    # 中：仅当每个真实出站更新均指向干净HEAD时，才能复用工作树门禁。
    $updates = @([Console]::In.ReadToEnd() -split '\r?\n' | Where-Object { $_ })
    if (-not $updates.Count) { Write-Output '[ok] No outgoing refs to approve'; return }
    $head = (Git-Lines @('rev-parse','HEAD'))[0]
    $branch = (Git-Lines @('symbolic-ref','--quiet','--short','HEAD'))[0]
    foreach ($update in $updates) {
        $parts = $update -split ' '
        if ($parts.Count -ne 4) { throw 'Git supplied an invalid pre-push ref update.' }
        $localRef, $localObject, $remoteRef, $remoteObject = $parts
        if ($remoteRef -ceq 'refs/heads/main') { throw 'Do not push main directly; merge the matching minor development branch through a pull request.' }
        if ($localObject -match '^0+$') { throw 'Ref deletions are not verified by this source hook.' }
        if ($remoteRef -clike 'refs/heads/*') {
            if ($branch -cne $DevelopmentBranch -or $localRef -cne "refs/heads/$branch" -or
                    $remoteRef -cne "refs/heads/$DevelopmentBranch" -or $localObject -cne $head) {
                throw "Push only the current clean HEAD from $DevelopmentBranch to the same remote branch."
            }
            if ($remoteObject -notmatch '^0+$') {
                & git -C $root merge-base --is-ancestor $remoteObject $head
                if ($LASTEXITCODE -ne 0) { throw 'Development branch updates must preserve the existing remote history.' }
            }
        }
        elseif ($remoteRef -clike 'refs/tags/*') {
            # EN: An explicitly pushed release tag may name main HEAD, never a development checkout.
            # 中：显式推送发行tag仅可指向main的HEAD，不可借开发检出发布。
            if ($branch -cne 'main' -or $localRef -cne $remoteRef -or $remoteObject -notmatch '^0+$' -or
                    (Git-Lines @('rev-parse',"$localObject^{commit}"))[0] -cne $head) {
                throw 'A new release tag must resolve to the current clean main HEAD.'
            }
            # EN: Peeling proves the commit, not the privacy of each annotated/signed tag object.
            # 中：peel只能证明目标提交；每层注释／签名tag对象仍须单独检查隐私。
            $tagObject = $localObject
            while ((Git-Lines @('cat-file','-t',$tagObject))[0] -ceq 'tag') {
                $tagText = (Git-Lines @('cat-file','tag',$tagObject)) -join "`n"
                Check-PrivacyText $tagText 'outgoing annotated tag'
                $header = ($tagText -split '\n\n', 2)[0]
                $tagger = [regex]::Match($header, '(?m)^tagger [^\r\n]* <([^>\r\n]+)> [0-9]+ [+-][0-9]{4}$')
                if (-not $tagger.Success) { Fail 'outgoing annotated tag : invalid tagger identity' }
                else { Check-Identity $tagger.Groups[1].Value 'outgoing annotated tag' }
                $target = [regex]::Match($header, '(?m)^object ([0-9a-f]+)$')
                if (-not $target.Success) { throw 'Outgoing annotated tag has no valid target object.' }
                $tagObject = $target.Groups[1].Value
            }
        }
        else { throw 'Only the matching minor development branch or an explicit main release tag can be verified.' }
    }
    $changes = Git-Lines @('status','--porcelain','--untracked-files=normal')
    if ($changes.Count) {
        throw 'Pre-push requires clean committed source matching the outgoing HEAD.'
    }
    Write-Output "[ok] Actual outgoing refs match clean HEAD ($branch)"
}
function Read-Xml([string]$Path) {
    $document = [Xml.XmlDocument]::new()
    $document.PreserveWhitespace = $true
    $document.LoadXml($utf8.GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xfeff))
    return $document
}
function Requires-CodeLicense([string]$Path) {
    return $Path -cmatch '^Source/FAF/.+\.(cs|csproj|props)$|^Build/([^/]+\.ps1|hooks/pre-push)$|^Tools/[^/]+\.(py|ps1)$|^Mod/.+\.xml$|^\.github/.+\.ya?ml$|^(\.gitignore|\.gitattributes|openspec/config\.yaml)$'
}
function Check-PrivacyText([string]$Text, [string]$Label) {
    # EN: Report locations, never print a matched credential or personal value.
    # 中：只报告位置，不回显命中的凭据或私人值。
    $patterns = [ordered]@{
        'local path' = '(?<![A-Za-z0-9/:\\])[A-Za-z]:[\\/]|(?<![A-Za-z0-9/:])/(?:Users|home)/[A-Za-z0-9._-]+'
        'credential' = 'ghp_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}'
        'private key' = ('-----' + 'BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----')
    }
    foreach ($kind in $patterns.Keys) {
        if ([regex]::IsMatch($Text, $patterns[$kind])) { Fail "$Label : $kind" }
    }
}
function Check-Identity([string]$Identity, [string]$Label) {
    # EN: Reserved .invalid domains and GitHub noreply identities disclose no personal mailbox.
    # 中：保留的.invalid域与GitHub noreply身份不披露私人邮箱。
    if ($Identity -notmatch '@[^@]+\.invalid$|@users\.noreply\.github\.com$|^noreply@github\.com$') {
        Fail "$Label : non-anonymous commit/tag email"
    }
}
function Check-Bytes([byte[]]$Bytes, [string]$Label) {
    # EN: Scan PNG metadata, not compressed pixel bytes that can resemble arbitrary strings.
    # 中：PNG检查元数据，不把压缩像素误作任意字符串扫描。
    if ($Bytes.Length -ge 8 -and [Convert]::ToHexString($Bytes[0..7]) -eq '89504E470D0A1A0A') {
        for ($offset = 8; $offset + 12 -le $Bytes.Length;) {
            $length = [uint64]$Bytes[$offset] * 16777216 + [uint64]$Bytes[$offset + 1] * 65536 +
                [uint64]$Bytes[$offset + 2] * 256 + [uint64]$Bytes[$offset + 3]
            if ($length -gt $Bytes.Length - $offset - 12) { Fail "$Label : malformed PNG chunk"; break }
            $kind = [Text.Encoding]::ASCII.GetString($Bytes, $offset + 4, 4)
            if ($kind -in @('tEXt','zTXt','iTXt','eXIf')) { Fail "$Label : textual/Exif PNG metadata" }
            $offset += [int]$length + 12
            if ($kind -eq 'IEND') { break }
        }
        return
    }
    $texts = [Collections.Generic.List[string]]::new()
    $texts.Add([Text.Encoding]::UTF8.GetString($Bytes))
    if ([Array]::IndexOf[byte]($Bytes, [byte]0) -ge 0) {
        # EN: Binary string heaps may begin at either byte alignment and use either UTF-16 byte order.
        # 中：二进制字符串堆可能从奇/偶字节起始，并使用任一UTF-16端序。
        foreach ($encoding in @([Text.Encoding]::Unicode, [Text.Encoding]::BigEndianUnicode)) {
            foreach ($offset in @(0,1)) {
                if ($Bytes.Length -gt $offset) { $texts.Add($encoding.GetString($Bytes, $offset, $Bytes.Length - $offset)) }
            }
        }
    }
    foreach ($text in $texts) {
        foreach ($match in [regex]::Matches($text, '[\x20-\x7e\p{L}]{6,}')) {
            Check-PrivacyText $match.Value $Label
        }
    }
}
function Keyed-Values([string]$Language) {
    $values = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root "Mod/Languages/$Language/Keyed") -Filter *.xml) {
        $document = Read-Xml $file.FullName
        foreach ($node in $document.DocumentElement.ChildNodes) {
            if ($node.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            if (-not $values.TryAdd($node.Name, $node.InnerText)) { Fail "$Language : duplicate Keyed $($node.Name)" }
        }
    }
    return ,$values
}
function Format-Indexes([string]$Text) {
    return (@([regex]::Matches($Text, '(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]*)?\}(?!\})') |
        ForEach-Object { $_.Groups[1].Value }) | Sort-Object) -join ','
}
function Check-Assembly([byte[]]$Bytes, [string]$Version, [string]$Label) {
    # EN: Read PE metadata without loading or executing game/mod code.
    # 中：只读PE元数据，不加载执行游戏或模组代码。
    $memory = [IO.MemoryStream]::new($Bytes, $false)
    $pe = [Reflection.PortableExecutable.PEReader]::new($memory)
    try {
        $reader = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        if ($reader.GetString($reader.GetAssemblyDefinition().Name) -cne 'FAF.FridgesAbsolutelyFly') { Fail "$Label : wrong assembly identity" }
        if ($reader.GetAssemblyDefinition().Version.ToString() -cne "$productVersion.0") { Fail "$Label : wrong product assembly version" }
        $game = @($reader.AssemblyReferences | ForEach-Object { $reader.GetAssemblyReference($_) } |
            Where-Object { $reader.GetString($_.Name) -ceq 'Assembly-CSharp' })
        if ($game.Count -ne 1 -or "$($game[0].Version.Major).$($game[0].Version.Minor)" -cne $Version) { Fail "$Label : wrong game reference version" }
    }
    finally { $pe.Dispose(); $memory.Dispose() }
}
function Check-History {
    # EN: Git object IDs select existing blobs; no new content hashes or history rewrites are made.
    # 中：Git对象ID只用于读取已有blob，不计算新hash，也不改写历史。
    $objects = Git-Lines @('rev-list','--objects','HEAD')
    $process = [Diagnostics.Process]::new()
    $process.StartInfo.FileName = 'git'
    $process.StartInfo.WorkingDirectory = $root
    $process.StartInfo.ArgumentList.Add('cat-file')
    $process.StartInfo.ArgumentList.Add('--batch')
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.UseShellExecute = $false
    $null = $process.Start()
    $stream = $process.StandardOutput.BaseStream
    $count = 0
    $finished = $false
    try {
        foreach ($entry in $objects) {
            $parts = $entry -split ' ', 2
            if ($parts.Count -ne 2) { continue }
            $process.StandardInput.WriteLine($parts[0]); $process.StandardInput.Flush()
            $header = [Text.StringBuilder]::new()
            do {
                $next = $stream.ReadByte()
                if ($next -lt 0) { throw 'Git object stream ended unexpectedly.' }
                if ($next -ne 10) { $null = $header.Append([char]$next) }
            } while ($next -ne 10)
            $metadata = $header.ToString().Split(' ')
            if ($metadata.Count -ne 3) { throw 'Git object header is invalid.' }
            $bytes = [byte[]]::new([int]$metadata[2])
            $read = 0
            while ($read -lt $bytes.Length) {
                $amount = $stream.Read($bytes, $read, $bytes.Length - $read)
                if ($amount -eq 0) { throw 'Git object content ended unexpectedly.' }
                $read += $amount
            }
            if ($stream.ReadByte() -ne 10) { throw 'Git object terminator is invalid.' }
            if ($metadata[1] -eq 'blob') {
                Check-SourcePath $parts[1] "history $($parts[1])"
                Check-Bytes $bytes "history $($parts[1])"
                $count++
            }
        }
        $process.StandardInput.Close(); $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw 'Git history reader failed.' }
        $finished = $true
    }
    finally {
        if (-not $finished -and -not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
    $messages = (Git-Lines @('log','HEAD','--format=%s%n%b')) -join [Environment]::NewLine
    Check-PrivacyText $messages 'commit messages'
    Check-PrivacyText ((Git-Lines @('for-each-ref','refs/tags','--format=%(contents)')) -join [Environment]::NewLine) 'tag messages'
    $identities = @((Git-Lines @('log','HEAD','--format=%ae%n%ce')) +
        (Git-Lines @('for-each-ref','refs/tags','--format=%(taggeremail)')) | Where-Object { $_ } | ForEach-Object { $_.Trim('<','>') })
    foreach ($identity in $identities) {
        Check-Identity $identity 'history'
    }
    Write-Output "[ok] HEAD history scanned: $count unique blobs"
}

# EN: Check the source checkout without requiring proprietary game/third-party assemblies.
# 中：源码检出门禁不要求私有游戏或第三方程序集。
$paths = Git-Lines @('ls-files','--cached','--others','--exclude-standard')
# EN: Audit the effective working tree: unstaged deletions/renames leave stale index paths, not live inputs.
# 中：审核实际工作树：未暂存的删除／更名会留下旧索引路径，但它们并非当前输入。
$deleted = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in (Git-Lines @('ls-files','--deleted'))) { $null = $deleted.Add($path) }
$paths = @($paths | Where-Object { -not $deleted.Contains($_) })
# EN: .gitignore is the public-tree boundary, including files accidentally force-added to the index.
# 中：.gitignore是公开目录边界，也检查误用force-add选入索引的文件。
$ignored = Git-Lines @('ls-files','--cached','--ignored','--exclude-standard')
foreach ($path in $ignored) {
    if (-not $deleted.Contains($path)) { Fail "$path : ignored local input selected for publication" }
}
$owned = Get-Content -LiteralPath (Join-Path $root 'ArtSource/distribution-manifest.json') -Raw | ConvertFrom-Json
$artPaths = @($paths | Where-Object { $_ -clike 'ArtSource/*' } | Sort-Object -Unique)
if (@(Compare-Object $artPaths @($owned.ownedFiles | Sort-Object -Unique)).Count) { Fail 'ArtSource : working-tree selection differs from ownedFiles' }
foreach ($path in $paths) {
    Check-SourcePath $path $path
    $full = Join-Path $root $path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Fail "$path : selected source file is missing"; continue }
    if ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { Fail "$path : source file is a link"; continue }
    Check-Bytes ([IO.File]::ReadAllBytes($full)) $path
    if ((Requires-CodeLicense $path) -and
            $utf8.GetString([IO.File]::ReadAllBytes($full)) -notmatch 'SPDX-License-Identifier: MPL-2\.0') {
        Fail "$path : missing MPL-2.0 source notice"
    }
}
# EN: Source-only verification checks the declaration; Package reuses this exact file for every channel.
# 中：仅源码门禁检查许可声明；Package为每个渠道复用这一个原始文件。
$licensePath = Join-Path $root 'LICENSE'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { Fail 'License : missing root LICENSE' }
else {
    $licenseText = $utf8.GetString([IO.File]::ReadAllBytes($licensePath))
    if ($licenseText -notmatch 'Mozilla Public License' -or $licenseText -notmatch 'Version 2\.0' -or
            $licenseText -notmatch '3\.1\.' -or $licenseText -notmatch '3\.2\.') {
        Fail 'License : root LICENSE is not the expected MPL-2.0 text'
    }
}
foreach ($inputPath in @($owned.toolInputs.PSObject.Properties | ForEach-Object { $_.Value })) {
    if ($inputPath -cnotin $owned.ownedFiles) { Fail "ArtSource : unowned tool input $inputPath" }
}
$documents = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'Mod') -Recurse -Filter *.xml) {
    try { $documents[$file.FullName] = Read-Xml $file.FullName }
    catch { Fail "$([IO.Path]::GetRelativePath($root, $file.FullName)) : invalid UTF-8/XML" }
}
$en = Keyed-Values 'English'; $zh = Keyed-Values 'ChineseSimplified'
if (@(Compare-Object @($en.Keys | Sort-Object) @($zh.Keys | Sort-Object)).Count) { Fail 'Keyed : English/Chinese key sets differ' }
foreach ($key in $en.Keys) {
    if ($zh.ContainsKey($key) -and (Format-Indexes $en[$key]) -cne (Format-Indexes $zh[$key])) { Fail "Keyed : placeholder mismatch $key" }
}
$textures = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'Mod/Textures') -Recurse -Filter *.png) {
    $null = $textures.Add([IO.Path]::GetRelativePath((Join-Path $root 'Mod/Textures'), $file.FullName).Replace('\','/'))
}
$prefixes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($document in $documents.Values) {
    foreach ($node in $document.SelectNodes('//texPath|//iconPath|//icon')) {
        if ($node.InnerText.StartsWith('FAF/', [StringComparison]::Ordinal)) { $null = $prefixes.Add($node.InnerText) }
    }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root 'Source/FAF') -Recurse -Filter *.cs) {
    $code = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($match in [regex]::Matches($code, 'ContentFinder<Texture2D>\.Get\("(FAF/[^"\r\n]+)"')) { $null = $prefixes.Add($match.Groups[1].Value) }
    foreach ($match in [regex]::Matches($code, '"(FAF_[A-Za-z0-9_.]+)"\s*\.Translate\s*\(')) {
        if (-not $en.ContainsKey($match.Groups[1].Value)) { Fail "Keyed : missing literal $($match.Groups[1].Value)" }
    }
}
foreach ($prefix in $prefixes) {
    if ($textures.Contains($prefix + '.png')) { continue }
    if (@('north','east','south','west' | Where-Object { -not $textures.Contains($prefix + '_' + $_ + '.png') }).Count) { Fail "Textures : unresolved prefix $prefix" }
}
$about = Read-Xml (Join-Path $root 'Mod/About/About.xml')
$project = Read-Xml (Join-Path $root 'Source/FAF/FAF.csproj')
$productVersion = [string]$project.Project.PropertyGroup.Version
if ($productVersion -notmatch '^\d+\.\d+\.\d+$' -or $about.ModMetaData.modVersion -cne $productVersion) {
    Fail 'Version : csproj Version and About modVersion must match'
}
else {
    $version = [version]$productVersion
    $developmentBranch = "$($version.Major).$($version.Minor).x"
    if ($PSBoundParameters.ContainsKey('PullRequestSourceBranch') -and $PullRequestSourceBranch -cne $developmentBranch) {
        Fail "Pull request : main requires source branch $developmentBranch for product $productVersion"
    }
    if ($PrePush) { Check-PushRefs $developmentBranch }
}
if ($about.ModMetaData.packageId -cne 'coahuilite.fridgesabsolutelyfly') { Fail 'About : stable packageId changed' }
if (@(Compare-Object @('1.6','1.7') @($about.ModMetaData.supportedVersions.li)).Count) { Fail 'About : supported versions differ' }
foreach ($required in @('brrainz.harmony','erdelf.HumanoidAlienRaces','keeptpa.NivarianRace','dkdcakedangao.NivarianSleepInFridgesQWQ')) {
    if ($required -cnotin @($about.ModMetaData.modDependencies.li | ForEach-Object packageId) -or
        $required -cnotin @($about.ModMetaData.loadAfter.li)) { Fail "About : missing required dependency/load order $required" }
}
$folders = Read-Xml (Join-Path $root 'Mod/LoadFolders.xml')
foreach ($version in '1.6','1.7') {
    $nodes = $folders.SelectNodes("/loadFolders/v$version/li")
    if (@(Compare-Object @('/',$version) @($nodes | ForEach-Object InnerText)).Count) { Fail "LoadFolders : unexpected $version mapping" }
}
if ($FullHistory) { Check-History }
if ($NotesFile) { Check-PrivacyText ($utf8.GetString([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($NotesFile)))) 'release notes' }

if ($Archive -or $PackageDirectory) {
    # EN: Consume Package.ps1's exact whitelist; do not maintain a second staging policy.
    # 中：直接消费Package.ps1同一白名单，不另建一份打包策略。
    $expected = @(& (Join-Path $PSScriptRoot 'Package.ps1') -ListInputs -Channel $Channel)
    $wanted = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in $expected) {
        $name = $item.Relative.Replace('\','/')
        $wanted.Add($name, $item)
        for ($slash = $name.LastIndexOf('/'); $slash -gt 0; $slash = $name.LastIndexOf('/', $slash - 1)) {
            $null = $directories.Add($name.Substring(0, $slash + 1))
        }
    }
    function Compare-PackageFile([string]$Name, [byte[]]$Bytes) {
        if (-not $wanted.ContainsKey($Name)) { Fail "Package : extra $Name"; return }
        $item = $wanted[$Name]
        $source = if ($item.Source) { [IO.File]::ReadAllBytes($item.Source) } else { $item.Content }
        if (-not [Linq.Enumerable]::SequenceEqual[byte]($Bytes, [byte[]]$source)) { Fail "Package : content/identity differs $Name" }
        if ($Name -match '^(1\.[67])/Assemblies/FAF\.FridgesAbsolutelyFly\.dll$') {
            Check-Assembly $Bytes $Matches[1] $Name
        }
        Check-Bytes $Bytes "package $Name"
    }
    if ($Archive) {
        $zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
        try {
            $files = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
            if ($files.Count -ne $wanted.Count) { Fail 'Archive : runtime file count differs' }
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($entry in $zip.Entries) {
                $prefix = 'FAF_FridgesAbsolutelyFly/'
                if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::Ordinal) -or $entry.FullName -match '\\|(^|/)\.\.(/|$)') {
                    Fail 'Archive : unsafe root/path'; continue
                }
                if (-not $seen.Add($entry.FullName)) { Fail "Archive : duplicate $($entry.FullName)"; continue }
                $name = $entry.FullName.Substring($prefix.Length)
                if ($entry.FullName.EndsWith('/')) {
                    if ($name -and -not $directories.Contains($name)) { Fail "Archive : extra directory $name" }
                    continue
                }
                $memory = [IO.MemoryStream]::new(); $inputStream = $entry.Open()
                try { $inputStream.CopyTo($memory); $bytes = $memory.ToArray() }
                finally { $inputStream.Dispose(); $memory.Dispose() }
                Compare-PackageFile $name $bytes
            }
            Write-Output "[scan] Actual $($Channel.ToLowerInvariant()) archive compared: $($files.Count) files"
        }
        finally { $zip.Dispose() }
    }
    else {
        $directory = [IO.Path]::GetFullPath($PackageDirectory)
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'Package directory is missing.' }
        for ($ancestor = $directory; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Package directory must not traverse links.'
            }
        }
        $files = 0
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Recurse -Force) {
            $name = [IO.Path]::GetRelativePath($directory, $entry.FullName).Replace('\','/')
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { Fail "Package : link $name"; continue }
            if ($entry.PSIsContainer) {
                if (-not $directories.Contains($name + '/')) { Fail "Package : extra directory $name" }
                continue
            }
            $files++
            $null = $seen.Add($name)
            Compare-PackageFile $name ([IO.File]::ReadAllBytes($entry.FullName))
        }
        foreach ($name in $wanted.Keys) { if (-not $seen.Contains($name)) { Fail "Package : missing $name" } }
        Write-Output "[scan] Actual $($Channel.ToLowerInvariant()) directory compared: $files files"
    }
}
if ($failures.Count) {
    foreach ($failure in $failures | Sort-Object -Unique) { Write-Output "[FAIL] $failure" }
    throw "Verification failed ($($failures.Count) findings). No publication was performed."
}
Write-Output "[ok] Source/content boundaries, $($documents.Count) XML files, $($en.Count) bilingual keys, $($prefixes.Count) static texture prefixes"
