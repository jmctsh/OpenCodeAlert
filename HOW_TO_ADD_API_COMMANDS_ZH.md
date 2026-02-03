# 如何添加新的 API 指令

本文档详细记录了在 OpenRA Copilot Mod 中添加新 API 指令的流程、架构说明以及开发经验总结。

## 1. API 架构简介

OpenRA Copilot 的 API 系统基于 Socket 通信，主要涉及以下几个核心文件：

*   **`OpenRA.Game/CopilotCommandServer.cs`**: 服务器入口，负责接收 Socket 请求、解析 JSON、验证基础格式、并路由到对应的处理函数。
*   **`OpenRA.Game/CopilotModels.cs`**: 定义请求和响应的数据模型，以及参数验证逻辑。
*   **`OpenRA.Mods.Common/ServerCommands.cs`**: 包含具体的指令处理逻辑（静态方法）。通常在这里解析参数并调用游戏内的逻辑。
*   **`OpenRA.Mods.Common/Traits/Copilot/`**: 对于复杂的指令（如 `expand_base`），通常会封装成一个 `Trait`（特性），挂载在 Player 或 World 上，由 `ServerCommands` 调用。

## 2. 添加指令的详细步骤

假设我们要添加一个名为 `my_command` 的新指令。

### 步骤 1: 定义参数验证 (CopilotModels.cs)

在 `OpenRA.Game/CopilotModels.cs` 中，找到 `ValidateCommandParams` 方法，添加新指令的参数检查逻辑。

```csharp
// CopilotModels.cs

public static (bool isValid, MCPError error) ValidateCommandParams(string command, JObject parameters)
{
    switch (command)
    {
        // ... 其他指令 ...
        case "my_command":
            return ValidateMyCommandParams(parameters);
        // ...
    }
}

// 编写具体的验证函数
private static (bool isValid, MCPError error) ValidateMyCommandParams(JObject parameters)
{
    // 示例：必须包含 targetId
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

### 步骤 2: 实现处理逻辑 (ServerCommands.cs)

在 `OpenRA.Mods.Common/ServerCommands.cs` 中添加静态处理方法。

```csharp
// ServerCommands.cs

public static string MyCommand(JObject json, World world)
{
    // 1. 解析玩家
    var player = ResolvePlayer(json, world);
    
    // 2. 解析参数
    var targetId = json["targetId"]?.ToObject<int>();
    
    // 3. 执行游戏逻辑
    // ...
    
    return "Command executed successfully";
}
```

### 步骤 3: 注册指令 (CopilotCommandServer.cs)

在 `OpenRA.Game/CopilotCommandServer.cs` 的 `WorldLoaded` 方法中注册该指令。

```csharp
// CopilotCommandServer.cs

public void WorldLoaded(World w, WorldRenderer wr)
{
    if (w.Type == WorldType.Regular && w.CopilotServer != null)
    {
        // ...
        w.CopilotServer.CommandHandlers["my_command"] = ServerCommands.MyCommand;
        // ...
    }
}
```

### 步骤 4: (可选) 实现复杂逻辑 Trait

如果指令涉及跨多个 Tick 的操作（如 `expand_base` 需要造车、移动、展开、造建筑），建议创建一个独立的 `Trait`。

1.  在 `OpenRA.Mods.Common/Traits/Copilot/` 下创建新文件（如 `MyComplexManager.cs`）。
2.  实现 `ITick` 接口以处理每帧逻辑。
3.  在 `ServerCommands.cs` 中获取该 Trait 并调用其方法。

## 3. 经验总结与避坑指南 (重要)

在开发 `expand_base` 指令的过程中，我们总结了以下关键经验，请务必阅读以避免走弯路：

### 3.1 必须在 YAML 中注册 Trait
**现象**：代码写得完美无缺，但在运行时调用指令返回 `Player does not have X trait`。
**原因**：C# 代码中定义了 Trait 只是第一步，**必须**在模组的规则文件（`rules/player.yaml` 或 `rules/world.yaml`）中显式添加该 Trait，否则它不会被加载到 Actor 上。
**对策**：
*   检查 `mods/ra/rules/player.yaml`
*   检查 `mods/cnc/rules/player.yaml`
*   确保你的 Trait 名称（如 `CopilotExpansionManager`）出现在 `Player` 定义下。

### 3.2 生产队列的“批处理”与“单次”
**现象**：请求建造多个建筑（如2个矿场），但只造了1个就不动了，或者逻辑卡死。
**原因**：旧的逻辑是“检查是否在造 -> 如果没在造 -> 发送一个建造指令”。但这会导致每次 Tick 都尝试发指令，或者造完一个后无法自动衔接下一个。
**对策**：
*   使用 `EnsureProduction(type, quantity)` 模式。
*   计算 `需建造总数 - (当前队列中数量 + 已完成未放置数量)`。
*   使用 `Order.StartProduction(actor, item, count)` 一次性发送剩余所需的数量，让引擎内部的队列系统去管理排队。

### 3.3 Order 的执行与 Target
**现象**：发送了 `Move` 或 `Deploy` 指令，但单位没有任何反应。
**原因**：
1.  **Target 构造错误**：`Target.FromCell` 和 `Target.FromActor` 必须正确使用。
2.  **Order 构造函数**：某些 Order（如 `DeployTransform`）需要特定的构造函数参数（如 `suppressVisualFeedback` 或 `queued`）。
3.  **队列阻塞**：如果不使用 `queued=false`（即立即执行），新指令可能会被追加到现有指令（如巡逻）之后而迟迟不执行。对于紧急指令，通常应设为不排队（覆盖当前指令）。

### 3.4 状态机管理长流程
**场景**：`expand_base` 需要：造MCV -> 等待MCV -> 移动MCV -> 展开 -> 造电厂 -> 放电厂 -> 造矿厂...
**经验**：不要试图在一个函数里做完。使用 `enum` 定义状态机（State Machine），在 `ITick.Tick` 中根据当前状态执行微小的一步。
*   **Waiting 状态**：每个动作发出后，进入对应的 Waiting 状态（如 `WaitingForMCV`）。
*   **超时/重试**：设置 `waitTicks`，避免每帧都高频检查，同时也作为简单的超时重试机制。

### 3.5 错误信息反馈
**经验**：API 返回的错误信息越详细越好。
*   不要只返回 "Failed"。
*   如果是缺前置，调用 `DescribeMissingPrerequisites` 返回具体缺什么（如 "缺少：重工厂"）。
*   如果是状态不对，返回当前正在做什么（如 "Base expansion already in progress"）。

### 3.6 跨 Mod 兼容性
**现象**：代码在 RA (红警) 下能跑，在 CNC (泰伯利亚之日) 下报错或无反应。
**原因**：不同 Mod 的 Actor 命名不同。例如电厂在 RA 里叫 `pwr` 或 `apwr`，在 CNC 里可能叫 `nukr`。
**对策**：在代码中定义别名数组，如 `string[] powerNames = { "POWR", "APWR", "PWR", "NUKR" };`，并遍历尝试解析。
