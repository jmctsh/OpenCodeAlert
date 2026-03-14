# OpenClaw 使用说明文档

本文档详细介绍了如何让 OpenClaw AI 自主进入游戏房间，并通过 TCP API 配置游戏参数（阵营、队伍、位置等）。

## 1. 启动与进房 (Join Game)

OpenClaw 可以通过命令行参数直接启动 OpenRA 客户端并连接到指定服务器。

### 启动命令格式

```bash
# Windows
OpenRA.Game.exe Game.Mod=copilot Launch.Connect=115.191.61.19:27940

# Linux / macOS
./launch-game.sh Game.Mod=copilot Launch.Connect=115.191.61.19:27940
```

*   `Game.Mod=copilot`: 指定使用 Copilot 模组。
*   `Launch.Connect=115.191.61.19:27940`: 连接到指定的比赛服务器。

### 示例

```python
import subprocess

def join_server():
    cmd = [
        "OpenRA.Game.exe", 
        "Game.Mod=copilot", 
        "Launch.Connect=115.191.61.19:27940"
    ]
    subprocess.Popen(cmd)
```

---

## 2. Linux 服务器部署指南 (Headless Mode)

在 Linux 服务器上运行游戏客户端通常需要图形界面支持。为了在纯命令行环境（Headless Server）中运行 OpenClaw，你需要使用 `Xvfb` 来模拟显示环境。

### 2.1 依赖安装

在 Debian/Ubuntu 系统上，你需要安装 Mono 运行时和 Xvfb：

```bash
# 安装 Mono (OpenRA 运行环境)
sudo apt update
sudo apt install mono-complete

# 安装 Xvfb (虚拟显示服务)
sudo apt install xvfb
```

### 2.2 获取游戏本体

OpenClaw 需要完整的 OpenRA 游戏文件才能运行。

**源码编译步骤**：

1.  **安装构建依赖** (Ubuntu/Debian)
    ```bash
    # 安装 git, make, unzip
    sudo apt install git make unzip
    
    # 安装 .NET SDK (用于编译)
    # 参考: https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu
    sudo apt-get update && \
    sudo apt-get install -y dotnet-sdk-6.0
    ```

2.  **拉取代码**
    ```bash
    git clone -b dev https://github.com/jmctsh/OpenCodeAlert.git
    cd OpenCodeAlert
    ```

3.  **编译与资源下载**
    ```bash
    # 下载依赖库并编译
    make all
    
    # 这一步会自动下载必要的资源文件
    ```


### 2.3 启动脚本 (Headless Launch)

使用 `xvfb-run` 命令来启动游戏客户端，这样它就不会因为找不到显示器而报错。

```bash
# 在 Linux 服务器上运行
xvfb-run -a ./launch-game.sh Game.Mod=copilot Launch.Connect=115.191.61.19:27940
```

*   `-a`: 自动寻找可用的显示编号。
*   `./launch-game.sh`: 确保该脚本有执行权限 (`chmod +x launch-game.sh`)。

### 2.4 Python 集成示例

你的 Python 脚本也应该通过 `xvfb-run` 来启动游戏进程：

```python
import subprocess
import os

def join_server_headless():
    # 确保使用 Linux 启动脚本
    game_executable = "./launch-game.sh"
    
    # 构建启动命令，前缀加上 xvfb-run
    cmd = [
        "xvfb-run", "-a",
        game_executable, 
        "Game.Mod=copilot", 
        "Launch.Connect=115.191.61.19:27940"
    ]
    
    # 启动进程
    print(f"Starting OpenRA headless: {' '.join(cmd)}")
    subprocess.Popen(cmd)

if __name__ == "__main__":
    join_server_headless()
```

---

## 3. 房间控制 API (Lobby API)

当 OpenClaw 进入房间（Lobby）后，客户端会开启一个 TCP 监听端口（默认 **7446**），允许外部程序通过 JSON 指令控制房间设置。

*   **端口**: 7446
*   **协议**: TCP / JSON
*   **编码**: UTF-8

### 3.1 请求格式 (Request)

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

### 3.2 响应格式 (Response)

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

## 4. 可用指令列表

### 4.1 基础控制

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `set_faction` | `{"faction": "russia"}` | 设置国家 (`russia`=苏联阵营, `germany`=盟军阵营) |
| `set_team` | `{"team": 1}` | 设置队伍 (0=无队伍, 1-4=队伍) |
| `set_spawn` | `{"spawn": 0}` | 设置出生点 (0=随机, 1=出生点A, 2=出生点B, 以此类推) |
| `set_spectator` | `{}` | 切换为观察者 |
| `set_ready` | `{"ready": true}` | 设置准备状态 (true/false) |

### 4.2 房主专用 (Host Only)

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `map` | `{"map": "hash_or_uid"}` | 切换地图 |
| `slot_open` | `{"slot": "Multi0"}` | 打开指定槽位 |
| `slot_close` | `{"slot": "Multi0"}` | 关闭指定槽位 |
| `slot_bot` | `{"slot": "Multi0", "botType": "rush"}` | 添加电脑玩家 |
| `kick` | `{"clientIndex": 1}` | 踢出玩家 |
| `start_game` | `{}` | 开始游戏 |

### 4.3 查询

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `get_lobby_info` | `{}` | 获取当前房间详细信息（玩家、槽位、地图等） |

---

## 5. Python 调用示例

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
