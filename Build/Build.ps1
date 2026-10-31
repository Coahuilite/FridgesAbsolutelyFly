#requires -Version 7.0
# SPDX-License-Identifier: MPL-2.0
# EN: Build one version using caller-supplied references; never copy game/dependency DLLs into the mod.
# 中：使用调用者提供的引用编译指定版本；绝不将游戏或前置 DLL 复制进模组。
param(
    [Parameter(Mandatory)][ValidateSet('1.6','1.7')][string]$GameVersion,
    [Parameter(Mandatory)][string]$GameReferencesDir,
    [Parameter(Mandatory)][string]$HarmonyAssemblyPath,
    [Parameter(Mandatory)][string]$UpstreamAssemblyPath,
    [switch]$WarningsAsErrors,
    [switch]$Rebuild
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# EN: Keep single options as arrays; untyped pipeline results would splat a string character by character.
# 中：单个选项也保持数组；未定型的管道结果会把字符串逐字符展开传参。
[string[]]$warningArguments = if ($WarningsAsErrors) { @('-warnaserror') } else { @() }
[string[]]$rebuildArguments = if ($Rebuild) { @('--no-incremental') } else { @() }
& dotnet build (Join-Path $root 'Source/FAF/FAF.csproj') -c Release --nologo `
    "-p:GameVersion=$GameVersion" "-p:GameReferencesDir=$GameReferencesDir" `
    "-p:HarmonyAssemblyPath=$HarmonyAssemblyPath" "-p:UpstreamAssemblyPath=$UpstreamAssemblyPath" @warningArguments @rebuildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed for RimWorld $GameVersion." }
$output = Join-Path $root "Mod/$GameVersion/Assemblies"
$unexpected = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'FAF.FridgesAbsolutelyFly.dll')
if ($unexpected.Count) { throw 'Unexpected build outputs in version Assemblies directory. Inspect before packaging.' }
