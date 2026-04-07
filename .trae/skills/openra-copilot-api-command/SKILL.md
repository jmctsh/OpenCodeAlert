---
name: "openra-copilot-api-command"
description: "Guides adding new OpenRA Copilot API commands. Invoke when implementing, registering, validating, or debugging Socket-based Copilot commands and related Traits."
---

# OpenRA Copilot API Command

在这个工作区中，当任务涉及为 OpenRA Copilot Mod 新增、修改、排查 API 指令时，使用这个技能。

## 何时使用

在以下场景调用这个技能：

- 用户要求新增一个 OpenRA Copilot API 指令
- 需要修改已有 Copilot 指令的参数验证、注册或执行逻辑
- 需要排查 Socket API 请求能到达服务端，但命令执行失败的问题
- 任务涉及 `CopilotCommandServer.cs`、`CopilotModels.cs`、`ServerCommands.cs` 或 `Traits/Copilot/`
- 某个复杂命令需要拆成跨多个 Tick 执行的 Trait 状态机

不要在纯地图编辑、纯 YAML 地图资源修改、或与 Copilot API 无关的通用游戏逻辑任务中调用这个技能。

## 核心架构

OpenRA Copilot 的 API 系统基于 Socket 通信，核心职责通常分布在以下文件：

- `OpenRA.Game/CopilotCommandServer.cs`
  - 服务端入口
  - 接收 Socket 请求
  - 解析 JSON
  - 验证基础格式
  - 将命令路由到对应处理函数

- `OpenRA.Game/CopilotModels.cs`
  - 定义请求与响应模型
  - 负责命令参数验证

- `OpenRA.Mods.Common/ServerCommands.cs`
  - 放置具体指令处理逻辑
  - 通常负责解析参数、解析玩家、调用游戏逻辑或 Trait

- `OpenRA.Mods.Common/Traits/Copilot/`
  - 存放复杂命令对应的 Trait
  - 适合封装跨多个 Tick 的长流程逻辑
  - 常见挂载位置是 `Player` 或 `World`

## 标准新增流程

假设要新增一个名为 `my_command` 的新指令，优先按以下顺序实现。

### 第 1 步：在 `CopilotModels.cs` 中补齐参数验证

在 `ValidateCommandParams` 中新增分支，并为命令编写专用校验函数。

```csharp
public static (bool isValid, MCPError error) ValidateCommandParams(string command, JObject parameters)
{
    switch (command)
    {
        case "my_command":
            return ValidateMyCommandParams(parameters);
    }
}

private static (bool isValid, MCPError error) ValidateMyCommandParams(JObject parameters)
{
    if (parameters == null || !parameters.ContainsKey("targetId"))
    {
        return (false, new MCPError
        {
            Code = "MISSING_PARAM",
            Message = "Missing parameter: targetId"
        });
    }

    return (true, null);
}
```

实现要点：

- 不要把参数校验延后到真正执行逻辑时才做
- 错误信息要明确指出缺失字段或格式问题
- 如果已有类似命令，优先复用现有校验风格

### 第 2 步：在 `ServerCommands.cs` 中实现命令逻辑

为新命令添加静态处理方法。

```csharp
public static string MyCommand(JObject json, World world)
{
    var player = ResolvePlayer(json, world);
    var targetId = json["targetId"]?.ToObject<int>();

    // 执行游戏逻辑

    return "Command executed successfully";
}
```

实现要点：

- 先解析玩家，再解析命令参数
- 尽量沿用已有的 `ResolvePlayer`、实体解析、命名风格和错误返回模式
- 简单命令直接在这里完成
- 复杂命令只在这里做参数入口与调度，把流程下沉到 Trait

### 第 3 步：在 `CopilotCommandServer.cs` 中注册命令

在 `WorldLoaded` 中把字符串命令名映射到处理函数。

```csharp
public void WorldLoaded(World w, WorldRenderer wr)
{
    if (w.Type == WorldType.Regular && w.CopilotServer != null)
    {
        w.CopilotServer.CommandHandlers["my_command"] = ServerCommands.MyCommand;
    }
}
```

实现要点：

- 注册名必须和请求里的命令名完全一致
- 如果命令逻辑已实现但没有注册，外部调用仍然会失败
- 修改后要检查是否只在正确的世界类型下注册

### 第 4 步：复杂逻辑改用 Trait

如果命令涉及跨多个 Tick 的操作，优先创建独立 Trait，而不是把所有逻辑塞进一个方法。

典型场景：

- 造单位
- 等待生产完成
- 控制移动
- 展开或部署
- 放置建筑
- 串联多个前置检查与状态切换

推荐做法：

1. 在 `OpenRA.Mods.Common/Traits/Copilot/` 下创建新的 Trait 文件
2. 实现 `ITick` 处理逐帧推进
3. 由 `ServerCommands.cs` 获取对应 Trait 并调用入口方法

## 长流程命令的设计原则

### 使用状态机管理流程

对于类似 `expand_base` 这样的长流程，使用状态机而不是单函数串行逻辑。

推荐模式：

- 使用 `enum` 表达流程状态
- 在 `ITick.Tick` 中根据状态推进一步
- 每次只做一个小动作
- 动作发出后切换到对应 Waiting 状态

示例状态：

- `WaitingForMCV`
- `MovingMCV`
- `WaitingForDeploy`
- `WaitingForPowerPlant`

### 使用等待与重试节流

不要每个 Tick 都高频重复发命令或高频检查完整条件。

推荐模式：

- 使用 `waitTicks` 做简单节流
- 把它既当作轮询间隔，也当作基础超时重试机制
- 对生产、移动、部署这类动作都设置明确等待窗口

## 经验总结与避坑指南

### 1. Trait 必须在 YAML 中注册

常见现象：

- 代码已经写好
- 调用命令时报错 `Player does not have X trait`

根本原因：

- 仅在 C# 中定义 Trait 还不够
- 必须在规则文件中显式挂载，否则 Actor 不会拥有它

检查位置：

- `mods/ra/rules/player.yaml`
- `mods/cnc/rules/player.yaml`
- 必要时检查 `rules/world.yaml`

检查重点：

- 确认 Trait 名称出现在正确的 Actor 定义下
- 例如 `CopilotExpansionManager` 是否真实挂在 `Player` 或 `World` 上

### 2. 生产队列优先使用批量补齐，而不是单次反复发单

常见现象：

- 目标是建造多个建筑
- 实际只造了一个
- 或者逻辑在 Tick 循环里不断重复下单

更可靠的模式是 `EnsureProduction(type, quantity)`：

- 计算目标总数
- 扣除队列中已有数量
- 扣除已完成但尚未放置的数量
- 一次性把剩余数量交给引擎队列系统处理

推荐思路：

- 使用 `需建造总数 - (当前队列数量 + 已完成未放置数量)`
- 调用 `Order.StartProduction(actor, item, count)` 一次下发剩余数

这样可以避免：

- 每 Tick 重复发单
- 造完第一个后无法自然衔接第二个
- 人工维护队列状态过于复杂

### 3. 正确构造 Order 与 Target

常见现象：

- 发送了 `Move` 或 `Deploy`
- 单位没有反应

优先检查以下几点：

1. `Target` 是否正确构造
   - `Target.FromCell`
   - `Target.FromActor`
   - 二者不能混用

2. `Order` 构造函数是否匹配具体命令
   - 某些命令如 `DeployTransform` 需要特殊参数
   - 例如视觉反馈参数或是否排队参数

3. 是否错误地把命令加入已有队列末尾
   - 紧急命令通常应设置为立即执行
   - 如果不覆盖当前队列，单位可能永远先执行旧命令

处理原则：

- 对移动、展开、脱离卡死状态这类强控制命令，优先考虑非排队执行

### 4. 错误信息必须具体

不要只返回：

- `Failed`
- `Command failed`

推荐返回：

- 缺少哪个参数
- 缺少哪个前置建筑或科技
- 当前流程正在执行什么
- 是否已有同类任务在进行中

优先模式：

- 如果缺前置，调用类似 `DescribeMissingPrerequisites`
- 如果流程已在运行，返回如 `Base expansion already in progress`

### 5. 做好多 Mod 兼容

常见现象：

- 在 RA 下正常
- 在 CNC 下报错或无反应

根本原因：

- 不同 Mod 的 Actor 名称不同

例如同类建筑可能存在多种名字：

```csharp
string[] powerNames = { "POWR", "APWR", "PWR", "NUKR" };
```

推荐做法：

- 对关键单位和建筑定义别名数组
- 解析时按别名依次尝试
- 不要把 RA 的命名硬编码成唯一来源

## 实施检查清单

完成新增命令后，至少检查以下几点：

- 参数验证已接入 `ValidateCommandParams`
- 命令处理函数已写入 `ServerCommands.cs`
- 命令已在 `CopilotCommandServer.cs` 注册
- 如果使用 Trait，已在对应 YAML 中挂载
- 错误返回信息足够明确
- 长流程逻辑已拆成状态机，而不是单函数硬写
- 对 RA 和 CNC 的命名差异做了兼容处理

## 回答此类任务时的建议

当你处理这类需求时，优先按下面的顺序工作：

1. 先定位命令入口、参数校验、注册表和实际处理逻辑
2. 判断这是简单即时命令还是复杂长流程命令
3. 简单命令直接在 `ServerCommands.cs` 实现
4. 长流程命令下沉到 Trait，并配套状态机
5. 最后检查 YAML 注册与跨 Mod 命名兼容

如果用户是在排查已有命令失败，优先检查：

1. 命令是否注册
2. 参数是否通过校验
3. Trait 是否实际挂载
4. Order 与 Target 是否构造正确
5. 是否被旧命令队列阻塞
