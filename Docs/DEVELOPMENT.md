# F.A.F. 开发与发布

从仓库根目录用调用者提供的版本引用构建。玩家使用[安装说明](../Mod/README.md)和[机制指南](MECHANICS.md)；开发行为合同见[OpenSpec](../openspec/README.md)，内部公式与参数见[模型管理](../openspec/models/cabinet-model.md)，图像重建见[ASSETS](ASSETS.md)。

## 源码与运行内容

| 位置 | 责任 |
| --- | --- |
| `Source/FAF/Core`、`Rendering`、`Integration` | 悬浮状态、原建筑绘制快照及前置冰箱判定 |
| `Source/FAF/Cabinet` | 真实储物、资源、寝具与住客；`Core` 保存纯温控数学，`Integration` 接原生机制 |
| `Source/FAF/Transport` | 席位／载重、原柜载荷移交、近场与世界航程、抵达及回收 |
| `Source/FAF/UI` | 货物、温控和姿态窗口，消费权威快照与动作 |
| `Source/FAF/Adapters/1.6`、`Adapters/1.7` | Unity／Waylith 等真正变化的版本接缝 |
| `Mod/` | Defs、双语文案、运行贴图、元数据及双版程序集输出 |
| `Build/`、`Tools/`、`ArtSource/` | 门禁／发行、美术导出及清单选定制作输入 |

自有名称使用 FAF，程序集为 `FAF.FridgesAbsolutelyFly`，包标识为 `coahuilite.fridgesabsolutelyfly`。N.S.I.F. 仍指前置，其 API、DLL 与 `RW*_NSIF` 引用变量保持原名。

## 构建

使用 PowerShell 7.4及以上（`pwsh`）与 .NET 10 SDK。两个版本的游戏引用和前置 DLL 必须分别匹配，不能把 1.6 的 DLL 用于 1.7。

- **1.6**：目标框架 `net472`；引用游戏的 `Assembly-CSharp.dll` 和 Unity 模块。
- **1.7**：目标框架 `net10.0`；引用 `Assembly-CSharp.dll`、`UnityStubs.dll`、`Waylith.dll`、`Waylith.UnityCompat.dll`。
- 两个版本均需对应的 `0Harmony.dll` 与 `NivarianSleepInFridges.dll`。

先在自己的环境中设置下列变量，再从模组根目录运行。变量只保存本机位置，不写入项目源码或更变记录。

```powershell
pwsh -File ./Build/Build.ps1 `
  -GameVersion 1.6 `
  -GameReferencesDir $env:RW16_REFERENCES `
  -HarmonyAssemblyPath $env:RW16_HARMONY `
  -UpstreamAssemblyPath $env:RW16_NSIF

pwsh -File ./Build/Build.ps1 `
  -GameVersion 1.7 `
  -GameReferencesDir $env:RW17_REFERENCES `
  -HarmonyAssemblyPath $env:RW17_HARMONY `
  -UpstreamAssemblyPath $env:RW17_NSIF
```

每个版本仅输出 `Mod/版本/Assemblies/FAF.FridgesAbsolutelyFly.dll`；中间文件进入忽略的 `.build/版本/`。DLL是构建产物，不进入Git。游戏位置只通过参数／环境变量传入。私有 `Build/Build.local.ps1` 可保存本机引用便捷入口，但该文件不跟踪、不分发；新环境仍按上面的公开参数构建。

两个版本构建完成后，为每次发行选择一个新标签：

```powershell
pwsh -File ./Build/Package.ps1 -ReleaseTag YOUR_NEW_TAG
```

输出为 `Release/YOUR_NEW_TAG/FAF_FridgesAbsolutelyFly.zip`，包内根仍为 `FAF_FridgesAbsolutelyFly`，其下直接是About/Defs等运行目录，不再套一层Mod。脚本拒绝已有标签、stage或ZIP；不清理、覆盖旧发行。省略标签仍使用 `Release/` 下的默认文件名，但已存在时同样拒绝覆盖。

## 状态权威与接缝

### 容器和运输使用同一批原物

本柜是 `Building_FlyingFridgeCabinet : Building_Storage, IThingHolder`，但主仓由 `contents` 实际持有，空的 `AllSlotCells` 避免退回地面货架储存。独立 `CompCabinetTransport : CompTransporter` 持有运输舱。

出发时，原柜及主仓以真实包裹进入唯一 `ActiveTransporterInfo`，运输舱的人货移交同一 Info；抵达再展开原柜并接回舱内。取消、失败、保存恢复都不能复制持有者或重新造人货。快照、索引及管理页不是第二份库存。

不能只附加 `CompShuttle` 作为标签：原生发射链会依此改变船体处置；普通弹射舱路径也不会自动保留原建筑。第三方若按具体 Comp 或类判断，需要针对实际调用点建立窄接缝。

### 子系统拥有规则，界面调用能力

资源层拥有温度、压力、一个原生电池和一个液仓；科研 profile 从原生完成状态派生，不另存一份解锁状态。电网沿原生结算，离网仍扣同一个电池，补液和机动仍扣同一个液仓。

床与生存沿前置真实代理床及原生需求／环境伤害接缝，不另 TickPawn。物品信息沿原生腐烂、温度损坏和孵化计算，不在界面模拟第二套计时器。

货物、温控和姿态窗口分别处理自身输入与布局；共用真实占用、资源和路线结果。时间热键由原生 TimeControls 处理，数框聚焦时避免把数字误当倍速键。

### 版本差异只留在实际变化处

共享玩法可以依赖稳定的 Verse／RimWorld 类型；纯数学放在 Core。绘制提交、变化的签名与必要 IL 锚点留在适配边界，不把整套玩法复制成两个版本。

前置判定器是 internal；集成边界缓存绑定 `FridgeDefUtility.IsFridgeDef(ThingDef, out string)` 的委托，不复制判定规则。签名失配应明确限制受影响能力。

悬浮不改共享 Def 的 drawerType。原建筑 Print 生成实例快照，按原生 Things 脏标记失效，落地、取消生成或移除地图时释放。实时附加层继续走原路径；`FlyingPrintLayer` 组合原生层，不继承会自动安装的 SectionLayer。

1.6 使用 Unity 提交；ASF 的可选 injected 路径只在绘制期间临时变换并恢复缓存。1.7 使用 Waylith `MeshDrawSubmission.Enqueue(in MeshDrawCommand)`，保留原材质、shader 与属性，不引入强制 ASF、额外 AssetBundle 或全局 shader 改写。

`GroundPowerWires` 在私有 Print 捕获时排除电线，并在真实地面接点绘制，不改变电网拓扑。柜体分层、透明开口、固定顶面偏移和导出比例统一见[美术维护](ASSETS.md)。

打包睡垫保留原 MinifiedThing 和 InnerThing，只切换图像；柜内按实际持有关系显示铺开形态。睡垫及本柜各阶段 UI 图标采用局部比例拟合，不预扭曲图片或临时修改共享 Def。

## 发行

### 开发分支与受保护的 main

开发分支按产品版本的 major.minor 命名：`0.1.0` 使用 `0.1.x`，补丁更新仍在同一分支；进入下一个 minor 时建立对应分支。日常修改提交到开发分支，通过 PR 合并到 `main`，不直接推送 `main`。

`main` 要求 PR、来自 GitHub Actions 的 `source` 检查成功且分支与目标保持最新，并解决所有审阅讨论；禁止强推和删除，管理员也受约束。发往 `main` 的 PR 来源必须是项目版本对应的开发分支。单维护者无需凑齐额外审批人数，合并前仍由维护者审阅并确认试玩结果。

Dev 用于开发分支试玩；Steam／GitHub／All 从干净的 `main` 准备。合并本身不创建 tag、不上传运行包，也不发布 Release。预发布和正式发布都使用已合并的源码，分别明确选择，不以分支推送代替发布。

### 准备全部渠道

按上方构建入口设置 `RW16_REFERENCES`、`RW16_HARMONY`、`RW16_NSIF` 及对应 `RW17` 变量，指向分别核实的引用；`RW*_NSIF` 指睡冰箱前置。

先审阅玩家文案、更新日志和版本，经开发分支 PR 合并后切换到干净的 `main`。从仓库根目录运行：

~~~powershell
pwsh -File ./Build/Release.ps1 -ReleaseTag YOUR_NEW_TAG -Channel All
~~~

`ReleaseTag` 是全新的候选目录名，只含字母、数字、横线及下划线，不是产品版本或 Git tag。脚本不覆盖已有候选。产品版本以 `Source/FAF/FAF.csproj` 的 `Version` 为人工主源，`Mod/About/About.xml` 的 `modVersion` 须同步，门禁会核对。

| 用途 | 候选内位置 |
| --- | --- |
| dev 本地测试 | `Release/YOUR_NEW_TAG/dev/FAF_FridgesAbsolutelyFly/` |
| Steam 上传源 | `Release/YOUR_NEW_TAG/steam/FAF_FridgesAbsolutelyFly/` |
| GitHub 玩家下载 | `Release/YOUR_NEW_TAG/FAF_FridgesAbsolutelyFly.zip` |
| 本地源码／产物绑定 | `Release/YOUR_NEW_TAG/release-receipt.json` |

All 对 1.6／1.7 各以零警告要求重编译一次，再沿同一 Package 白名单生成三种产物并核对实际目录／ZIP。玩家产物只含运行内容及必要说明，不附 Source、ArtSource、PDB、游戏／前置 DLL、外部原图或开发日志。

每种产物的 `About/BuildInfo.json` 记录产品版本、渠道、源码提交、dirty 状态与可读身份，不含私人路径。三渠道的 About.modVersion 相同，开发身份不另改 XML 版本号。

All、Steam 和 GitHub 要求干净的 `main`。单独 Dev 可从 `main` 或对应 minor 开发分支准备，并允许未提交状态；产物明确显示 dev／dirty，不能用于 GitHub 草稿发布：

~~~powershell
pwsh -File ./Build/Release.ps1 -ReleaseTag dev-YYYYMMDD -Channel Dev
~~~

省略 Channel 时为 GitHub；只需一渠时可选 Steam 或 GitHub。查看动作而不构建、建目录或联网：

~~~powershell
pwsh -File ./Build/Release.ps1 -ReleaseTag preview-only -Channel All -WhatIf
~~~

收据绑定真实源码、引用和产物。准备候选后如再修改任何受跟踪内容，应重新提交并使用新标签准备，不把旧收据重标为新提交。候选、日志和收据只留本地。

### 许可与源码获取

源码和软件配置按逐文件SPDX标记适用[MPL-2.0](../LICENSE)。图像不纳入该许可，视觉与依赖范围统一见[随包声明](../Mod/THIRD_PARTY_NOTICES.md)。

Package从根LICENSE放入完整许可证，并用同一BuildInfo身份生成SOURCE_NOTICE.md。正式构建指向精确源码提交；dirty Dev只给基点，不能将它当作未提交修改的完整源码。

**公开分发二进制前，先确认SOURCE_NOTICE所指的精确源码提交已在公开仓库可获取。** 若分发带有未提交修改的开发构建，还须提供实际修改源码。源码仍不必塞入玩家ZIP，Source／ArtSource分发边界保持。

### 推送源码，不上传私有引用

~~~powershell
pwsh -File ./Build/Verify.ps1 -FullHistory
~~~

本地 pre-push hook 读取 Git 实际出站的 ref 和提交，只接受干净当前 HEAD：开发分支须符合产品版本的 major.minor，`main` 直推和本地归档分支均被拒绝；发布标签须指向干净 `main` 的当前提交。待推对象不等于当前已验证源码时，先切换到对应提交完成检查，不绕过 hook。标签附加信息也须通过隐私检查。

GitHub Actions 复用源码／内容／历史检查；发往 `main` 的 PR 另外核对来源开发分支。云端没有私有游戏／前置 DLL，不声称完成游戏编译。`.gitignore` 限定公开文档与现行规格；忽略的本地记录即使用 force-add 选入也会被门禁拒绝。

新 clone 需要 hook 时显式设置，不覆盖已有用户配置：

~~~powershell
git config --local core.hooksPath Build/hooks
~~~

目标仓库为 [Coahuilite/FridgesAbsolutelyFly](https://github.com/Coahuilite/FridgesAbsolutelyFly)。源码推送与 tag／Release 是分开的动作；脚本不自动建仓、改可见性、创建 tag 或 push。不把未明确授权的历史重写或正式发布夹带进源码推送。

### Steam 上传使用 Steam 目录

上传器选择 `steam/FAF_FridgesAbsolutelyFly`，不选择 dev、GitHub ZIP 或仓库根。上传由维护者执行；脚本不启动游戏、操作 Steam 登录、接受协议或发布页面。

上传器写入的 `PublishedFileId.txt` 只留在忽略的上传目录，不复制回 Mod 或 Git，也不加入玩家 ZIP。正文从[工坊页面维护稿](WORKSHOP.md)选择对应语言，仅复制其BBCode代码块内的内容，更新说明从[英文日志](CHANGELOG.md)或[中文日志](CHANGELOG.zh-CN.md)对应版本复制。完整说明增删改，不因介绍文案的悬念而省略实际功能。实际上线后才将待发布标记改为发布日期。

### 单独创建 GitHub 草稿发行

确认候选来自干净的 `main`、授权仓库及相应源 tag 已另行推送后，将发布说明放入忽略的候选目录。以产品版本 0.1.0 为例：

~~~powershell
pwsh -File ./Build/Release.ps1 -Publish `
  -Receipt ./Release/YOUR_NEW_TAG/release-receipt.json `
  -Repository Coahuilite/FridgesAbsolutelyFly -Tag v0.1.0 `
  -NotesFile ./Release/YOUR_NEW_TAG/release-notes.md
~~~

此入口只接受收据中的 GitHub 候选，核对当前干净 `main`、实际包，以及远端 `main` 和 tag 均指向收据中的源码提交，再创建草稿。预发布候选可使用 `v0.1.0-rc.1` 一类 tag，并在同一调用中加 `-Prerelease`；它仍只创建草稿。产品 `Version`／`About.modVersion` 保持 `0.1.0`，预发布标志属于 GitHub 发行身份。

不把 Steam 或 dev 产物当作 GitHub 包。维护者审阅草稿后，再显式发布为 prerelease 或正式 release；脚本不自动公开。失败先查看实际草稿，不自动覆盖资产或重发。

使用正常 Git／gh 认证，不提取令牌。gh 不在 PATH 时可用 `-GitHubCli` 指定现有程序，不自动安装。Steam 上传、GitHub 草稿、正式发行分别确认和执行，不能相互代替。
