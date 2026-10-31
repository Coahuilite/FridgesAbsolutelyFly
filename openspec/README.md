# F.A.F. 规格入口

此页是行为合同的唯一人类入口。按能力、生产者—消费者连接及原生接缝查阅正文；结构、构建与发布见[开发手册](../Docs/DEVELOPMENT.md)。

## 如何阅读

- `specs/` 是 RimWorld 1.6／1.7 的现行行为合同。实现偏差应修复，不能倒改合同来宣称完成。
- 规格与进度分开。要求和 Scenario 描述应有行为，不表示已经执行过测试。
- `SHALL`／`MUST` 表示强制保证，`SHOULD` 表示可有理由例外的建议，`MAY` 表示可选项。
- 单柜活体占用精确为 **0或1个 pawn**；空柜合法，无人世界出发仍由科研与路线资格决定。
- 变更差量须明确采用后才更新主规格。代码、编译、原生验收和发布是不同事实。

## 按责任查正文

### 内聚能力

| 事实或操作 | 现行规格 | 稳定要求ID |
|---|---|---|
| 真实冷藏仓、质量与睡垫 | [cold-storage](specs/cold-storage/spec.md) | STO-01～04 |
| 内温、气氛、电池、液仓和自动供液 | [thermal-resources](specs/thermal-resources/spec.md) | RES-01～06 |
| 乘员、床资格与正常生存 | [cabin-residency](specs/cabin-residency/spec.md) | OCC-01～04 |
| 所有兼容冰箱的悬浮与旋转 | [fridge-motion](specs/fridge-motion/spec.md) | MOV-01～03 |
| 近场、世界、轨道、聚落投送与回收 | [cabinet-transport](specs/cabinet-transport/spec.md) | TRN-01～06 |
| 货物、温控、姿态、目标选择、复合资源表、冷却和快捷离柜 | [player-interface](specs/player-interface/spec.md) | UI-01～08 |
| 科研、基础睡垫、材料、视觉家族与资产来源 | [research-presentation](specs/research-presentation/spec.md) | RSH-01～06 |
| 玩家包、公开仓库、三渠道与轻量发行 | [player-distribution](specs/player-distribution/spec.md) | CMP-03～06 |

### 生产者—消费者连接

| 连接 | 现行规格 | 维护责任 |
|---|---|---|
| 真实环境／资源结算 → 物品、乘员与界面 | [environment-consumption](specs/environment-consumption/spec.md) | 环境／资源维护者负责连接，实际消费者履行采用义务 |
| 装载／原柜 → 航程／抵达 → 管理访问 | [payload-handoff](specs/payload-handoff/spec.md) | 持有／运输维护者负责连接，接收和界面履行采用义务 |

连接页定义状态如何被真实消费者采用；数值、资格和资源规则只在对应能力页维护，不要求另建总管、消息队列或第二份状态。

### 原生适配接缝

[native-integration](specs/native-integration/spec.md)持有 CMP-01／02：双版、可选 DLC、前置及保存兼容。具体类、方法和补丁锚点留在实现及[开发手册](../Docs/DEVELOPMENT.md)，不写进玩法要求。

## 核心是能力与连接的组合

~~~text
本体科研／前置冰箱判定 → 能力与资格
真实储存／住客 → 持有与载荷移交 → 近场／世界／抵达／回收
柜内环境与实际资源结算 → 原生物品／乘员机制
上述真实状态与动作许可 → 独立货物、温控、姿态和目标界面
版本／前置适配 → 以上各自的原生接缝
~~~

组合不创建第二份库存、乘员或资源余额。

## 内部模型与参数

[冷柜模型](models/cabinet-model.md)集中维护默认参数、温控／续航计算及世界航程曲线。它属于规格管理资料，不是玩家指南；与主规格有重合的约束须一起维护。读者机制解释放在[机制指南](../Docs/MECHANICS.md)，不在其中复制内部参数台账。

## 轻量维护

直接维护 Markdown，不要求安装规格平台。行为规则、设计解释、执行待办和实际证据各自独立；项目过程记录只留本地，不作为公开规格的依赖。

如已有 OpenSpec CLI，可选择运行结构检查：

~~~powershell
openspec validate --all --strict --no-interactive
~~~

该检查不决定规则采用，也不证明游戏运行成功，不是源码 CI 的强制依赖。
