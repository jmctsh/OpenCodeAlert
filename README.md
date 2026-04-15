# OpenClaw AI 接入文档

本文档面向 AI Agent、自动化脚本和服务进程，介绍如何以无头方式启动 OpenClaw，并通过 TCP API 控制房间参数（阵营、队伍、位置、开局等）。

## 1. 使用原则

本项目默认面向自动化环境，而不是面向真人交互界面。

- 推荐始终使用 `Game.Platform=Headless` 运行。
- 推荐通过脚本或进程管理器拉起客户端。
- 推荐在进入房间后通过 TCP API 完成配置，而不是依赖人工点击。
- Windows 下优先使用仓库自带的 `launch-game.cmd` 作为启动入口。

---

## 2. 获取与编译

OpenClaw 需要完整的 OpenRA 游戏文件才能运行。在 Windows 上可以使用 `.NET SDK` 或 `Visual Studio` 编译。

### 2.1 获取源码

```cmd
git clone -b dev_win https://github.com/jmctsh/OpenCodeAlert.git
cd OpenCodeAlert
```

### 2.2 构建运行文件

```cmd
make.cmd all
```

该命令会下载依赖、准备资源，并生成运行 OpenClaw 所需的二进制文件与 Headless 平台支持文件。

如果你更习惯 IDE，也可以直接打开 `OpenRA.sln`，使用 `Release` 配置生成解决方案。

---

## 3. 无头启动与进房

### 3.1 推荐启动命令

在仓库根目录的 `PowerShell` 中执行：

```powershell
.\launch-game.cmd Game.Mod=copilot Game.Platform=Headless Launch.Connect=115.191.61.19:27940
```

参数说明：

- `Game.Mod=copilot`: 使用 Copilot 模组。
- `Game.Platform=Headless`: 启用无头渲染平台，不创建图形窗口和音频设备。
- `Launch.Connect=115.191.61.19:27940`: 启动后自动连接指定服务器。

说明：

- `launch-game.cmd` 是当前仓库在 Windows 下的推荐入口。
- 如果尚未完成构建，上述命令不会成功，因为运行文件尚未生成。
- 在 `PowerShell` 中必须显式写成 `.\launch-game.cmd`，不能直接写 `launch-game.cmd`。

### 3.2 Python 启动示例

```python
import subprocess
from pathlib import Path


def join_server_headless(repo_dir: str):
    repo = Path(repo_dir)
    launcher = repo / "launch-game.cmd"

    cmd = [
        str(launcher),
        "Game.Mod=copilot",
        "Game.Platform=Headless",
        "Launch.Connect=115.191.61.19:27940",
    ]

    print(f"Starting OpenClaw headless: {' '.join(cmd)}")
    subprocess.Popen(cmd, cwd=repo)


if __name__ == "__main__":
    join_server_headless(r"D:\OpenCodeAlert")
```

### 3.3 运行建议

- 在服务器、容器或 CI 环境中，始终使用无头模式。
- 建议由外部守护进程统一管理启动、重启和日志采集。
- 如果需要多实例运行，应为每个实例分配独立的工作目录、日志目录和端口策略。

---

## 4. 房间控制 API (Lobby API)

当 OpenClaw 进入房间（Lobby）后，客户端会开启一个 TCP 监听端口（默认 **7446**），允许外部程序通过 JSON 指令控制房间设置。

*   **端口**: 7446
*   **协议**: TCP / JSON
*   **编码**: UTF-8

### 4.1 请求格式 (Request)

```json
{
    "apiVersion": "1.0",
    "requestId": "unique_id_123",
    "command": "command_name",
    "params": {
        "param1": "value1"
    }
}
```

### 4.2 响应格式 (Response)

```json
{
    "status": 1,          // 1: 成功, -1: 失败
    "requestId": "unique_id_123",
    "response": "Success message",
    "data": { ... },      // 返回数据（查询命令）
    "error": {            // 错误信息（如果有）
        "code": "ERROR_CODE",
        "message": "Error description"
    }
}
```

---

## 5. 可用指令列表

### 5.1 基础控制

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `set_faction` | `{"faction": "russia"}` | 设置国家 (`russia`=苏联阵营, `germany`=盟军阵营) |
| `set_team` | `{"team": 1}` | 设置队伍 (0=无队伍, 1-4=队伍) |
| `set_spawn` | `{"spawn": 0}` | 设置出生点 (0=随机, 1=出生点A, 2=出生点B, 以此类推) |
| `set_spectator` | `{}` | 切换为观察者 |
| `set_ready` | `{"ready": true}` | 设置准备状态 (true/false) |

### 5.2 房主专用 (Host Only)

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `map` | `{"map": "hash_or_uid"}` | 切换地图 |
| `slot_open` | `{"slot": "Multi0"}` | 打开指定槽位 |
| `slot_close` | `{"slot": "Multi0"}` | 关闭指定槽位 |
| `slot_bot` | `{"slot": "Multi0", "botType": "rush"}` | 添加电脑玩家 |
| `kick` | `{"clientIndex": 1}` | 踢出玩家 |
| `start_game` | `{}` | 开始游戏 |

### 5.3 查询

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `get_lobby_info` | `{}` | 获取当前房间详细信息（玩家、槽位、地图等） |

---

## 6. Python 调用示例

```python
import socket
import json
import time

class LobbyAPI:
    def __init__(self, host='127.0.0.1', port=7446):
        self.address = (host, port)

    def send_command(self, command, params=None):
        request = {
            "apiVersion": "1.0",
            "requestId": str(time.time()),
            "command": command,
            "params": params or {}
        }
        
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.connect(self.address)
                s.sendall(json.dumps(request).encode('utf-8'))
                data = s.recv(16384)
                return json.loads(data.decode('utf-8'))
        except ConnectionRefusedError:
            print("无法连接到 Lobby API，请确保游戏已进入房间")
            return None

# 使用示例
if __name__ == "__main__":
    api = LobbyAPI()
    
    # 1. 获取房间信息
    info = api.send_command("get_lobby_info")
    print("房间信息:", json.dumps(info, indent=2, ensure_ascii=False))
    
    # 2. 选择苏联阵营国家（russia）
    api.send_command("set_faction", {"faction": "russia"})
    
    # 3. 选择队伍 1
    api.send_command("set_team", {"team": 1})
    
    # 4. 准备
    api.send_command("set_ready", {"ready": True})
    
    # 5. (如果是房主) 开始游戏
    # api.send_command("start_game")
```
