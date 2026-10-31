#requires -Version 7.4
# SPDX-License-Identifier: MPL-2.0
<#
EN: Prepare builds and audits a new local candidate. -Publish uploads that exact candidate as a draft.
中：默认构建并核验新的本地候选；-Publish只将该精确候选上传为草稿。
EN: Git commits, remotes, tags and pushes remain separate maintainer operations.
中：Git提交、远端、tag及push仍是单独的维护者操作。
#>
[CmdletBinding(DefaultParameterSetName = 'Prepare', SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Prepare')][string]$ReleaseTag,
    [Parameter(ParameterSetName = 'Prepare')][ValidateSet('Dev','Steam','GitHub','All')][string]$Channel = 'GitHub',
    [Parameter(ParameterSetName = 'Prepare')][string]$RW16References = $env:RW16_REFERENCES,
    [Parameter(ParameterSetName = 'Prepare')][string]$RW16Harmony = $env:RW16_HARMONY,
    [Parameter(ParameterSetName = 'Prepare')][string]$RW16Upstream = $env:RW16_NSIF,
    [Parameter(ParameterSetName = 'Prepare')][string]$RW17References = $env:RW17_REFERENCES,
    [Parameter(ParameterSetName = 'Prepare')][string]$RW17Harmony = $env:RW17_HARMONY,
    [Parameter(ParameterSetName = 'Prepare')][string]$RW17Upstream = $env:RW17_NSIF,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][switch]$Publish,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][string]$Receipt,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9_.-]*$')][string]$Repository,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][string]$Tag,
    [Parameter(Mandatory, ParameterSetName = 'Publish')][string]$NotesFile,
    [Parameter(ParameterSetName = 'Publish')][switch]$Prerelease,
    [Parameter(ParameterSetName = 'Publish')][string]$GitHubCli = 'gh'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Git-One([string[]]$Arguments) {
    $result = @(& git -C $root @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0 -or $result.Count -ne 1) { throw "Git read failed: $($Arguments[0])" }
    return ([string]$result[0]).Trim()
}
function Require-CleanSource {
    $changes = @(& git -C $root status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $changes.Count) { throw 'Commit or preserve source changes before preparing/publishing a release.' }
}
function Require-SourceBranch([bool]$Formal) {
    # EN: Check before any build/upload; Package independently guards its standalone entry point.
    # 中：在构建／上传前检查；Package另保护其独立调用入口。
    $branch = Git-One @('symbolic-ref','--quiet','--short','HEAD')
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'Source/FAF/FAF.csproj') -Raw
    $version = [version]([string]$project.Project.PropertyGroup.Version)
    $developmentBranch = "$($version.Major).$($version.Minor).x"
    if ($branch -cne 'main' -and ($Formal -or $branch -cne $developmentBranch)) {
        throw "Formal channels and publication require main; Dev may also use $developmentBranch."
    }
}
function Resolve-ProjectPath([string]$Relative) {
    $full = [IO.Path]::GetFullPath((Join-Path $root $Relative))
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release receipt paths must remain inside this project.'
    }
    return $full
}
function Check-OutputRoots {
    # EN: A build must not turn a historical workspace link into an unintended game deployment.
    # 中：构建不能借历史工作区链接意外写入游戏安装副本。
    foreach ($relative in @('Mod/1.6/Assemblies','Mod/1.7/Assemblies','.build/1.6','.build/1.7','Release')) {
        for ($path = Resolve-ProjectPath $relative; ; $path = Split-Path -Parent $path) {
            if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Release output roots must not traverse directory links.'
            }
            if ($path.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { break }
        }
    }
}
function Check-ReferenceSet([string]$Version, [string]$Directory, [string]$Harmony, [string]$Upstream) {
    foreach ($value in @($Directory, $Harmony, $Upstream)) {
        if ([string]::IsNullOrWhiteSpace($value)) { throw "Set all RW$($Version.Replace('.','')) reference parameters/environment variables." }
    }
    $game = Join-Path $Directory 'Assembly-CSharp.dll'
    foreach ($path in @($game, $Harmony, $Upstream)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "RimWorld $Version reference input is missing. Supply matching local references; nothing is downloaded." }
    }
    # EN: Verify real assembly identity; a directory named 1.6 can still contain the currently installed 1.7.
    # 中：核对真实程序集身份；名为1.6的目录仍可能实际指向已切换的1.7。
    $identity = [Reflection.AssemblyName]::GetAssemblyName([IO.Path]::GetFullPath($game))
    if ("$($identity.Version.Major).$($identity.Version.Minor)" -cne $Version) { throw "RimWorld $Version references contain the wrong Assembly-CSharp version." }
    if ([Reflection.AssemblyName]::GetAssemblyName([IO.Path]::GetFullPath($Harmony)).Name -cne '0Harmony' -or
        [Reflection.AssemblyName]::GetAssemblyName([IO.Path]::GetFullPath($Upstream)).Name -cne 'NivarianSleepInFridges') {
        throw "RimWorld $Version prerequisite assembly identity differs."
    }
    return [pscustomobject]@{ Version = $Version; GameAssembly = $identity.Version.ToString() }
}

if ($PSCmdlet.ParameterSetName -eq 'Prepare') {
    if ($ReleaseTag -notmatch '\A(?!(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z)[A-Za-z0-9][A-Za-z0-9_-]*\z') { throw 'ReleaseTag must follow Package.ps1 safe-folder naming.' }
    $releaseDirectory = Resolve-ProjectPath "Release/$ReleaseTag"
    if (Test-Path -LiteralPath $releaseDirectory) { throw 'ReleaseTag already exists; choose a new candidate instead of overwriting it.' }
    if (-not $PSCmdlet.ShouldProcess("Release/$ReleaseTag ($Channel)", 'Build both versions once, stage the selected channels, verify and write the source receipt')) { return }
    $formal = $Channel -ne 'Dev'
    Require-SourceBranch $formal
    if ($formal) { Require-CleanSource }
    Check-OutputRoots
    $commit = Git-One @('rev-parse','HEAD')
    $references = @(
        (Check-ReferenceSet '1.6' $RW16References $RW16Harmony $RW16Upstream),
        (Check-ReferenceSet '1.7' $RW17References $RW17Harmony $RW17Upstream)
    )
    foreach ($inputSet in @(
        @{ GameVersion = '1.6'; GameReferencesDir = $RW16References; HarmonyAssemblyPath = $RW16Harmony; UpstreamAssemblyPath = $RW16Upstream },
        @{ GameVersion = '1.7'; GameReferencesDir = $RW17References; HarmonyAssemblyPath = $RW17Harmony; UpstreamAssemblyPath = $RW17Upstream }
    )) {
        & (Join-Path $PSScriptRoot 'Build.ps1') @inputSet -WarningsAsErrors -Rebuild
    }
    if ($formal) { Require-CleanSource }
    if ((Git-One @('rev-parse','HEAD')) -cne $commit) { throw 'Source commit changed during the release build.' }
    # EN: Build two game targets, not six flavors; stage the same DLLs under separate channel identities.
    # 中：只编译两个游戏目标，不编六套渠道；同一DLL按独立渠道身份暂存。
    [string[]]$channels = if ($Channel -eq 'All') { @('Dev','Steam','GitHub') } else { @($Channel) }
    $artifacts = [Collections.Generic.List[object]]::new()
    foreach ($selected in $channels) {
        & (Join-Path $PSScriptRoot 'Package.ps1') -ReleaseTag $ReleaseTag -Channel $selected
        $channelName = $selected.ToLowerInvariant()
        $parent = "Release/$ReleaseTag"
        if ($channelName -ne 'github') { $parent += "/$channelName" }
        $directory = "$parent/FAF_FridgesAbsolutelyFly"
        $archive = if ($channelName -eq 'github') { "$parent/FAF_FridgesAbsolutelyFly.zip" } else { $null }
        $verifyArguments = @{ Channel = $selected }
        if ($archive) { $verifyArguments.Archive = Resolve-ProjectPath $archive }
        else { $verifyArguments.PackageDirectory = Resolve-ProjectPath $directory }
        # EN: Read history once per candidate; every actual channel output still receives its own audit.
        # 中：每个候选只读一次历史；每个实际渠道产物仍分别核验。
        if ($selected -eq $channels[-1]) { $verifyArguments.FullHistory = $true }
        & (Join-Path $PSScriptRoot 'Verify.ps1') @verifyArguments
        $identity = Get-Content -LiteralPath (Resolve-ProjectPath "$directory/About/BuildInfo.json") -Raw | ConvertFrom-Json
        $artifacts.Add([ordered]@{
            channel = $channelName
            version = $identity.version
            sourceCommit = $identity.sourceCommit
            dirty = $identity.dirty
            displayVersion = $identity.displayVersion
            packageDirectory = $directory
            archive = $archive
        })
    }
    if ($formal) { Require-CleanSource }
    if ((Git-One @('rev-parse','HEAD')) -cne $commit) { throw 'Source changed while the candidate was being packaged or verified.' }
    $github = @($artifacts | Where-Object { $_.channel -eq 'github' })
    $record = [ordered]@{
        schemaVersion = 2
        version = $artifacts[0].version
        sourceCommit = $commit
        archive = if ($github.Count) { $github[0].archive } else { $null }
        releaseTag = $ReleaseTag
        artifacts = $artifacts.ToArray()
        references = $references
        createdUtc = [DateTime]::UtcNow.ToString('o')
        meaning = 'Both game targets built once; actual channel outputs and source identities audited. Dev may be dirty. No game acceptance, Steam upload or publication is implied.'
    }
    $receiptPath = Join-Path $releaseDirectory 'release-receipt.json'
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
    foreach ($artifact in $artifacts) { Write-Output "Candidate ($($artifact.channel) / $($artifact.displayVersion)): $($artifact.packageDirectory)" }
    Write-Output "Receipt: Release/$ReleaseTag/release-receipt.json"
    return
}

# EN: Publication never creates a repository/tag, pushes Git, replaces assets, or makes a release public.
# 中：发布不建仓/tag、不push、不替换资产，也不把草稿转公开。
if (-not $PSCmdlet.ShouldProcess("$Repository / $Tag", 'Verify the prepared candidate and create a GitHub draft release with its ZIP')) { return }
Require-CleanSource
Require-SourceBranch $true
$record = Get-Content -LiteralPath ([IO.Path]::GetFullPath($Receipt)) -Raw | ConvertFrom-Json
if ($record.schemaVersion -notin @(1,2) -or $record.sourceCommit -cne (Git-One @('rev-parse','HEAD'))) {
    throw 'Receipt must name the current clean source commit; prepare the final committed candidate first.'
}
# EN: Only a clean GitHub channel ZIP can become a draft asset, never a Dev or Steam stage.
# 中：草稿资产只接受干净的GitHub渠道ZIP，不接受Dev或Steam目录。
if ($record.schemaVersion -eq 2) {
    $github = @($record.artifacts | Where-Object { $_.channel -ceq 'github' })
    if ($github.Count -ne 1 -or $github[0].dirty -or $github[0].sourceCommit -cne $record.sourceCommit -or
        $github[0].archive -cne $record.archive -or $github[0].version -cne $record.version) {
        throw 'Receipt must contain one clean GitHub archive bound to its source and product version.'
    }
}
if ([string]::IsNullOrWhiteSpace($record.archive)) { throw 'This candidate contains no publishable GitHub archive.' }
$archivePath = Resolve-ProjectPath $record.archive
if ($record.archive -cne "Release/$($record.releaseTag)/FAF_FridgesAbsolutelyFly.zip") { throw 'Receipt has an unexpected archive layout.' }
& (Join-Path $PSScriptRoot 'Verify.ps1') -FullHistory -Archive $archivePath -Channel GitHub -NotesFile $NotesFile
$command = Get-Command -Name $GitHubCli -CommandType Application -ErrorAction SilentlyContinue
if ($command) { $GitHubCli = $command.Source }
elseif ($GitHubCli -eq 'gh' -and $env:ProgramFiles -and (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'GitHub CLI/gh.exe'))) {
    $GitHubCli = Join-Path $env:ProgramFiles 'GitHub CLI/gh.exe'
}
else { throw 'GitHub CLI is unavailable; supply -GitHubCli or add the installed gh to PATH. No installation is attempted.' }
# EN: A local branch named main is insufficient; publication must match the authorized remote main.
# 中：本地分支名为main仍不足；发布源码必须与授权仓库真实main完全一致。
$mainReference = @(& $GitHubCli api "repos/$Repository/git/ref/heads/main")
if ($LASTEXITCODE -ne 0) { throw 'Cannot read main from the authorized GitHub repository.' }
$mainTarget = ($mainReference -join [Environment]::NewLine | ConvertFrom-Json).object
if ($mainTarget.type -cne 'commit' -or $mainTarget.sha -cne $record.sourceCommit) {
    throw 'The authorized remote main must match the prepared source commit; merge the corresponding minor branch through its pull request first.'
}
& git -C $root check-ref-format "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw 'Tag is not a valid Git tag name.' }
$escapedTag = [Uri]::EscapeDataString($Tag)
$reference = @(& $GitHubCli api "repos/$Repository/git/ref/tags/$escapedTag")
if ($LASTEXITCODE -ne 0) { throw 'The exact remote source tag must already exist.' }
$target = ($reference -join [Environment]::NewLine | ConvertFrom-Json).object
while ($target.type -eq 'tag') {
    $annotated = @(& $GitHubCli api "repos/$Repository/git/tags/$($target.sha)")
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the annotated remote source tag.' }
    $target = ($annotated -join [Environment]::NewLine | ConvertFrom-Json).object
}
if ($target.type -ne 'commit' -or $target.sha -cne $record.sourceCommit) {
    throw 'The existing remote tag does not resolve to the prepared source commit. Push the intended source/tag separately.'
}
[string[]]$publishArguments = @('release','create',$Tag,$archivePath,'--repo',$Repository,'--verify-tag','--draft','--title',$Tag,'--notes-file',([IO.Path]::GetFullPath($NotesFile)))
if ($Prerelease) { $publishArguments += '--prerelease' }
& $GitHubCli @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Draft creation/upload failed. Inspect the remote draft before an explicit resume; no asset is overwritten automatically.' }
Write-Output 'Draft created. Review it on GitHub before publishing; no public release was requested.'
