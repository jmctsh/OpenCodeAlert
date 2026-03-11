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

## 2. 房间控制 API (Lobby API)

当 OpenClaw 进入房间（Lobby）后，客户端会开启一个 TCP 监听端口（默认 **7446**），允许外部程序通过 JSON 指令控制房间设置。

*   **端口**: 7446
*   **协议**: TCP / JSON
*   **编码**: UTF-8

### 2.1 请求格式 (Request)

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

### 2.2 响应格式 (Response)

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

## 3. 可用指令列表

### 3.1 基础控制

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `set_faction` | `{"faction": "soviet"}` | 设置己方阵营 (soviet, allies) |
| `set_team` | `{"team": 1}` | 设置队伍 (0=无队伍, 1-4=队伍) |
| `set_spawn` | `{"spawn": 0}` | 设置出生点 (0-N) |
| `set_spectator` | `{}` | 切换为观察者 |
| `set_ready` | `{"ready": true}` | 设置准备状态 (true/false) |

### 3.2 房主专用 (Host Only)

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `map` | `{"map": "hash_or_uid"}` | 切换地图 |
| `slot_open` | `{"slot": "Multi0"}` | 打开指定槽位 |
| `slot_close` | `{"slot": "Multi0"}` | 关闭指定槽位 |
| `slot_bot` | `{"slot": "Multi0", "botType": "rush"}` | 添加电脑玩家 |
| `kick` | `{"clientIndex": 1}` | 踢出玩家 |
| `start_game` | `{}` | 开始游戏 |

### 3.3 查询

| 命令 | 参数 | 说明 |
| :--- | :--- | :--- |
| `get_lobby_info` | `{}` | 获取当前房间详细信息（玩家、槽位、地图等） |

---

## 4. Python 调用示例

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
    
    # 2. 选择苏联阵营
    api.send_command("set_faction", {"faction": "soviet"})
    
    # 3. 选择队伍 1
    api.send_command("set_team", {"team": 1})
    
    # 4. 准备
    api.send_command("set_ready", {"ready": True})
    
    # 5. (如果是房主) 开始游戏
    # api.send_command("start_game")
```
