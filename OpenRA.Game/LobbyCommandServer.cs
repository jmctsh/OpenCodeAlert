using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenRA.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace OpenRA
{
    public class LobbyCommandServer
    {
        Socket serverSocket;
        readonly int port;
        OrderManager orderManager;
        bool isRunning;

        public const string CurrentApiVersion = "1.0";
        public bool DebugMode { get; set; } = false;

        private const int MaxRetryAttempts = 5;
        private const int RetryDelayMs = 1000;
        private int currentRetryCount = 0;

        public delegate string CommandHandler(JObject json, OrderManager orderManager);
        public delegate JObject QueryHandler(JObject json, OrderManager orderManager);

        public Dictionary<string, CommandHandler> CommandHandlers = new();
        public Dictionary<string, QueryHandler> QueryHandlers = new();

        static readonly Dictionary<string, Dictionary<string, string>> ErrorMessages = new()
        {
            ["INVALID_REQUEST"] = new()
            {
                ["en"] = "Invalid JSON format",
                ["zh"] = "无效的JSON格式"
            },
            ["INVALID_VERSION"] = new()
            {
                ["en"] = "Unsupported API version, current version: {0}",
                ["zh"] = "不支持的API版本，当前版本: {0}"
            },
            ["COMMAND_EXECUTION_ERROR"] = new()
            {
                ["en"] = "Command execution failed",
                ["zh"] = "命令执行失败"
            },
            ["QUERY_EXECUTION_ERROR"] = new()
            {
                ["en"] = "Query execution failed",
                ["zh"] = "查询执行失败"
            },
            ["INVALID_COMMAND"] = new()
            {
                ["en"] = "Unknown command",
                ["zh"] = "未知的命令"
            },
            ["INTERNAL_ERROR"] = new()
            {
                ["en"] = "Server internal error",
                ["zh"] = "服务器内部错误"
            }
        };

        static string GetErrorMessage(string errorCode, string language, params object[] args)
        {
            if (string.IsNullOrEmpty(language) || !ErrorMessages.ContainsKey(errorCode) || !ErrorMessages[errorCode].ContainsKey(language))
            {
                language = "zh";
            }
            var messageTemplate = ErrorMessages[errorCode][language];
            return string.Format(messageTemplate, args);
        }

        public LobbyCommandServer(int port, OrderManager orderManager)
        {
            this.port = port;
            this.orderManager = orderManager;

            // Register built-in commands for headless mode
            QueryHandlers["get_lobby_info"] = GetLobbyInfo;
            CommandHandlers["set_faction"] = SetFaction;
            CommandHandlers["set_team"] = SetTeam;
            CommandHandlers["set_spawn"] = SetSpawn;
            CommandHandlers["set_ready"] = SetReady;
            CommandHandlers["start_game"] = StartGame;
            CommandHandlers["chat"] = SendChat;
        }

        // Built-in query implementations
        JObject GetLobbyInfo(JObject json, OrderManager om)
        {
            var info = new JObject();
            if (om?.LobbyInfo == null)
                return info;

            var lobbyInfo = om.LobbyInfo;
            info["isHost"] = Game.IsHost;
            info["localClientIndex"] = om.LocalClient?.Index;

            var slots = new JArray();
            foreach (var slot in lobbyInfo.Slots)
            {
                var s = new JObject();
                s["name"] = slot.Key;
                s["closed"] = slot.Value.Closed;
                s["allowBots"] = slot.Value.AllowBots;
                s["lockFaction"] = slot.Value.LockFaction;
                s["lockTeam"] = slot.Value.LockTeam;
                s["required"] = slot.Value.Required;
                var client = lobbyInfo.ClientInSlot(slot.Key);
                if (client != null)
                {
                    var c = new JObject();
                    c["index"] = client.Index;
                    c["name"] = client.Name;
                    c["faction"] = client.Faction;
                    c["team"] = client.Team;
                    c["spawnPoint"] = client.SpawnPoint;
                    c["isReady"] = client.IsReady;
                    c["isAdmin"] = client.IsAdmin;
                    c["isBot"] = client.Bot != null;
                    s["client"] = c;
                }
                slots.Add(s);
            }
            info["slots"] = slots;

            var clients = new JArray();
            foreach (var client in lobbyInfo.Clients)
            {
                var c = new JObject();
                c["index"] = client.Index;
                c["name"] = client.Name;
                c["faction"] = client.Faction;
                c["team"] = client.Team;
                c["spawnPoint"] = client.SpawnPoint;
                c["isReady"] = client.IsReady;
                c["isAdmin"] = client.IsAdmin;
                c["isBot"] = client.Bot != null;
                c["slot"] = client.Slot;
                clients.Add(c);
            }
            info["clients"] = clients;
            info["map"] = lobbyInfo.GlobalSettings.Map;
            info["serverName"] = lobbyInfo.GlobalSettings.ServerName;

            return info;
        }

        // Built-in command implementations
        string SetFaction(JObject json, OrderManager om)
        {
            var faction = json["faction"]?.ToString();
            if (string.IsNullOrEmpty(faction))
                return "Missing faction parameter";
            var clientIndex = GetLocalClientIndex(om);
            om.IssueOrder(Order.Command($"faction {clientIndex} {faction}"));
            return "Faction set";
        }

        string SetTeam(JObject json, OrderManager om)
        {
            var team = json["team"]?.ToObject<int>();
            if (team == null)
                return "Missing team parameter";
            var clientIndex = GetLocalClientIndex(om);
            om.IssueOrder(Order.Command($"team {clientIndex} {team.Value}"));
            return "Team set";
        }

        string SetSpawn(JObject json, OrderManager om)
        {
            var spawn = json["spawn"]?.ToObject<int>();
            if (spawn == null)
                return "Missing spawn parameter";
            var clientIndex = GetLocalClientIndex(om);
            om.IssueOrder(Order.Command($"spawn {clientIndex} {spawn.Value}"));
            return "Spawn set";
        }

        string SetReady(JObject json, OrderManager om)
        {
            var ready = json["ready"]?.ToObject<bool>() ?? true;
            var state = ready ? "Ready" : "NotReady";
            om.IssueOrder(Order.Command($"state {state}"));
            return $"Ready set to {ready}";
        }

        string StartGame(JObject json, OrderManager om)
		{
			om.IssueOrder(Order.Command("startgame"));
			return "StartGame command sent";
		}

        string SendChat(JObject json, OrderManager om)
        {
            var text = json["text"]?.ToString();
            if (string.IsNullOrEmpty(text))
                return "Missing text parameter";
            om.IssueOrder(Order.Chat(text));
            return $"Chat sent: {text}";
        }

        int GetLocalClientIndex(OrderManager om)
        {
            return om?.LocalClient?.Index ?? 0;
        }

        public void UpdateOrderManager(OrderManager orderManager)
        {
            this.orderManager = orderManager;
        }

        ~LobbyCommandServer()
        {
            End();
        }

        public void Start()
        {
            isRunning = true;
            _ = Task.Run(() => StartServerLoop());
        }

        private async Task StartServerLoop()
        {
            while (isRunning)
            {
                try
                {
                    await StartServerInternal();
                    currentRetryCount = 0;
                }
                catch (Exception ex)
                {
                    LogError($"服务器启动失败: {ex.Message}");
                    if (!isRunning)
                        break;
                    currentRetryCount++;
                    if (currentRetryCount >= MaxRetryAttempts)
                    {
                        LogError($"已达到最大重试次数{MaxRetryAttempts}，停止重试");
                        break;
                    }
                    var delay = RetryDelayMs * currentRetryCount;
                    LogError($"将在 {delay}ms 后进行第 {currentRetryCount} 次重试..");
                    await Task.Delay(delay);
                }
            }
        }

        private async Task StartServerInternal()
        {
            try
            {
                serverSocket?.Close();
                serverSocket?.Dispose();
            }
            catch (Exception ex)
            {
                LogError($"清理旧socket时出错: {ex.Message}");
            }

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            try
            {
                serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
                serverSocket.Listen(10);
                LogInfo($"LobbyCommandServer 成功启动，监听端口 {port}");

                while (isRunning && serverSocket.IsBound)
                {
                    try
                    {
                        var clientSocket = await serverSocket.AcceptAsync();
                        LogInfo("接受新的客户端连接");
                        _ = Task.Run(() => HandleClientSafely(clientSocket));
                    }
                    catch (SocketException) when (!isRunning)
                    {
                        LogInfo("服务器正在停止，退出Accept循环");
                        break;
                    }
                    catch (ObjectDisposedException) when (!isRunning)
                    {
                        LogInfo("Socket已被释放，退出Accept循环");
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogError($"Accept客户端连接时发生异常: {ex.Message}");
                        throw;
                    }
                }
            }
            catch (SocketException ex)
            {
                LogError($"Socket操作失败: {ex.Message}");
                throw;
            }
        }

        public void End()
        {
            if (isRunning)
            {
                isRunning = false;
                try
                {
                    serverSocket?.Close();
                    serverSocket?.Dispose();
                    LogInfo("LobbyCommandServer 已停止");
                }
                catch (Exception ex)
                {
                    LogError($"停止服务器时出错: {ex.Message}");
                }
            }
        }

        private async Task HandleClientSafely(Socket clientSocket)
        {
            try
            {
                await HandleClient(clientSocket);
            }
            catch (Exception ex)
            {
                LogError($"处理客户端时发生异常: {ex.Message}");
                try
                {
                    clientSocket?.Close();
                    clientSocket?.Dispose();
                }
                catch (Exception closeEx)
                {
                    LogError($"关闭客户端连接时出错: {closeEx.Message}");
                }
            }
        }

        private void LogInfo(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[{timestamp}] [INFO] LobbyCommandServer: {message}");
        }

        private void LogError(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[{timestamp}] [ERROR] LobbyCommandServer: {message}");
        }

        async Task HandleClient(Socket clientSocket)
        {
            using (clientSocket)
            {
                try
                {
                    if (clientSocket == null)
                    {
                        throw new ArgumentException("clientSocket Uninit");
                    }

                    var buffer = new byte[16384];
                    var received = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                    var jsonString = Encoding.UTF8.GetString(buffer, 0, received);

                    if (DebugMode)
                    {
                        Console.WriteLine("=== 接收到的数据 ===");
                        Console.WriteLine(jsonString);
                        Console.WriteLine("====================");
                    }

                    MCPRequest request;
                    try
                    {
                        request = JsonConvert.DeserializeObject<MCPRequest>(jsonString);
                    }
                    catch (JsonException)
                    {
                        SendErrorResponse(clientSocket, new MCPError
                        {
                            Code = MCPErrorCodes.InvalidRequest,
                            Message = GetErrorMessage("INVALID_REQUEST", "zh")
                        }, null, DebugMode);
                        return;
                    }

                    var language = request.Language ?? "zh";
                    if (language is not "en" and not "zh")
                        language = "zh";

                    if (string.IsNullOrEmpty(request.ApiVersion))
                    {
                        SendErrorResponse(clientSocket, new MCPError
                        {
                            Code = MCPErrorCodes.InvalidVersion,
                            Message = GetErrorMessage("INVALID_VERSION", language, "N/A")
                        }, null, DebugMode);
                        return;
                    }

                    if (request.ApiVersion != CurrentApiVersion)
                    {
                        SendErrorResponse(clientSocket, new MCPError
                        {
                            Code = MCPErrorCodes.InvalidVersion,
                            Message = GetErrorMessage("INVALID_VERSION", language, CurrentApiVersion)
                        }, null, DebugMode);
                        return;
                    }

                    if (request.Params == null)
                        request.Params = new JObject();

                    var currentOrderManager = orderManager;
                    if (currentOrderManager == null)
                    {
                        SendErrorResponse(clientSocket, new MCPError
                        {
                            Code = MCPErrorCodes.InternalError,
                            Message = GetErrorMessage("INTERNAL_ERROR", language)
                        }, request.RequestId, DebugMode);
                        return;
                    }

                    if (CommandHandlers.TryGetValue(request.Command, out var commandHandler))
                    {
                        try
                        {
                            var result = commandHandler?.Invoke(request.Params, currentOrderManager);
                            SendSuccessResponse(clientSocket, result, request.RequestId, null, DebugMode);
                        }
                        catch (Exception ex)
                        {
                            var detail = new JObject
                            {
                                ["message"] = ex.Message,
                                ["stack"] = ex.StackTrace ?? "",
                                ["toString"] = ex.ToString()
                            };
                            SendErrorResponse(clientSocket, new MCPError
                            {
                                Code = MCPErrorCodes.CommandExecutionError,
                                Message = GetErrorMessage("COMMAND_EXECUTION_ERROR", language),
                                Details = detail
                            }, request.RequestId, DebugMode);
                        }
                    }
                    else if (QueryHandlers.TryGetValue(request.Command, out var queryHandler))
                    {
                        try
                        {
                            var resultJson = queryHandler?.Invoke(request.Params, currentOrderManager);
                            SendSuccessResponse(clientSocket, null, request.RequestId, resultJson, DebugMode);
                        }
                        catch (Exception ex)
                        {
                            var detail = new JObject
                            {
                                ["message"] = ex.Message,
                                ["stack"] = ex.StackTrace ?? "",
                                ["toString"] = ex.ToString()
                            };
                            SendErrorResponse(clientSocket, new MCPError
                            {
                                Code = MCPErrorCodes.CommandExecutionError,
                                Message = GetErrorMessage("COMMAND_EXECUTION_ERROR", language),
                                Details = detail
                            }, request.RequestId, DebugMode);
                        }
                    }
                    else
                    {
                        SendErrorResponse(clientSocket, new MCPError
                        {
                            Code = MCPErrorCodes.InvalidCommand,
                            Message = GetErrorMessage("INVALID_COMMAND", language)
                        }, request.RequestId, DebugMode);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"HandleClient中发生未处理的异常: {ex.Message}");
                    SendErrorResponse(clientSocket, new MCPError
                    {
                        Code = MCPErrorCodes.InternalError,
                        Message = GetErrorMessage("INTERNAL_ERROR", "zh")
                    }, null, DebugMode);
                }
            }
        }

        static void SendSuccessResponse(Socket clientSocket, string message = null, string requestId = null, JObject data = null, bool debugMode = false)
        {
            try
            {
                var response = new MCPResponse
                {
                    Status = 1,
                    RequestId = requestId,
                    Response = message,
                    Data = data
                };
                var responseJson = JsonConvert.SerializeObject(response);
                var buffer = Encoding.UTF8.GetBytes(responseJson);
                if (clientSocket.Connected)
                {
                    _ = clientSocket.Send(buffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 发送成功响应时异常: {ex.Message}");
            }
        }

        static void SendErrorResponse(Socket clientSocket, MCPError error, string requestId = null, bool debugMode = false)
        {
            try
            {
                var response = new MCPResponse
                {
                    Status = -1,
                    RequestId = requestId,
                    Error = error
                };
                var responseJson = JsonConvert.SerializeObject(response);
                var buffer = Encoding.UTF8.GetBytes(responseJson);
                if (clientSocket.Connected)
                {
                    _ = clientSocket.Send(buffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 发送错误响应时异常: {ex.Message}");
            }
        }
    }

}
