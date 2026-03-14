using Newtonsoft.Json.Linq;
using OpenRA.Network;
using System;
using System.Linq;
using OpenRA;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public static class LobbyCommands
	{
		static int ResolveClientIndex(JObject json, OrderManager orderManager)
		{
			var index = json["clientIndex"]?.ToObject<int>();
			if (index != null)
				return index.Value;

			if (orderManager.LocalClient == null)
				throw new InvalidOperationException("Local client not found");

			return orderManager.LocalClient.Index;
		}

		public static void Register(LobbyCommandServer server)
		{
			server.CommandHandlers["set_faction"] = SetFaction;
			server.CommandHandlers["set_team"] = SetTeam;
			server.CommandHandlers["set_spawn"] = SetSpawn;
			server.CommandHandlers["set_spectator"] = SetSpectator;
			server.CommandHandlers["set_ready"] = SetReady;
			server.CommandHandlers["start_game"] = StartGame;
			server.CommandHandlers["map"] = SetMap;
			server.CommandHandlers["slot_bot"] = AddBot;
			server.CommandHandlers["slot_open"] = OpenSlot;
			server.CommandHandlers["slot_close"] = CloseSlot;
			server.CommandHandlers["kick"] = Kick;
			
			server.QueryHandlers["get_lobby_info"] = GetLobbyInfo;
		}

		public static string SetFaction(JObject json, OrderManager orderManager)
		{
			var faction = json["faction"]?.ToString();
			if (string.IsNullOrEmpty(faction))
				throw new ArgumentException("Missing faction parameter");

			var clientIndex = ResolveClientIndex(json, orderManager);
			orderManager.IssueOrder(Order.Command($"faction {clientIndex} {faction}"));
			return "Faction set order issued";
		}

		public static string SetTeam(JObject json, OrderManager orderManager)
		{
			var team = json["team"]?.ToObject<int>();
			if (team == null)
				throw new ArgumentException("Missing team parameter");

			var clientIndex = ResolveClientIndex(json, orderManager);
			orderManager.IssueOrder(Order.Command($"team {clientIndex} {team.Value}"));
			return "Team set order issued";
		}

		public static string SetSpawn(JObject json, OrderManager orderManager)
		{
			var spawn = json["spawn"]?.ToObject<int>();
			if (spawn == null)
				throw new ArgumentException("Missing spawn parameter");

			var clientIndex = ResolveClientIndex(json, orderManager);
			orderManager.IssueOrder(Order.Command($"spawn {clientIndex} {spawn.Value}"));
			return "Spawn set order issued";
		}
		
		public static string SetSpectator(JObject json, OrderManager orderManager)
		{
			orderManager.IssueOrder(Order.Command("spectate"));
			return "Spectator set order issued";
		}

		public static string SetReady(JObject json, OrderManager orderManager)
		{
			var ready = json["ready"]?.ToObject<bool>() ?? true;
			var state = ready ? Session.ClientState.Ready : Session.ClientState.NotReady;
			orderManager.IssueOrder(Order.Command($"state {state}"));
			return $"Ready state set to {ready}";
		}

		public static string StartGame(JObject json, OrderManager orderManager)
		{
			if (!Game.IsHost)
				throw new InvalidOperationException("Only host can start game");
				
			orderManager.IssueOrder(Order.Command("startgame"));
			return "Start game order issued";
		}

		public static string SetMap(JObject json, OrderManager orderManager)
		{
			if (!Game.IsHost)
				throw new InvalidOperationException("Only host can change map");

			var mapUid = json["map"]?.ToString();
			if (string.IsNullOrEmpty(mapUid))
				throw new ArgumentException("Missing map parameter");

			orderManager.IssueOrder(Order.Command($"map {mapUid}"));
			return "Map set order issued";
		}

		public static string AddBot(JObject json, OrderManager orderManager)
		{
			if (!Game.IsHost)
				throw new InvalidOperationException("Only host can add bots");

			var slot = json["slot"]?.ToString();
			var controllerClientIndex = orderManager.LocalClient.Index; // Assume local player controls the bot
			var botType = json["botType"]?.ToString();

			if (string.IsNullOrEmpty(slot) || string.IsNullOrEmpty(botType))
				throw new ArgumentException("Missing slot or botType parameter");

			orderManager.IssueOrder(Order.Command($"slot_bot {slot} {controllerClientIndex} {botType}"));
			return "Add bot order issued";
		}

		public static string OpenSlot(JObject json, OrderManager orderManager)
		{
			if (!Game.IsHost)
				throw new InvalidOperationException("Only host can open slots");

			var slot = json["slot"]?.ToString();
			if (string.IsNullOrEmpty(slot))
				throw new ArgumentException("Missing slot parameter");

			orderManager.IssueOrder(Order.Command($"slot_open {slot}"));
			return "Slot open order issued";
		}

		public static string CloseSlot(JObject json, OrderManager orderManager)
		{
			if (!Game.IsHost)
				throw new InvalidOperationException("Only host can close slots");

			var slot = json["slot"]?.ToString();
			if (string.IsNullOrEmpty(slot))
				throw new ArgumentException("Missing slot parameter");

			orderManager.IssueOrder(Order.Command($"slot_close {slot}"));
			return "Slot close order issued";
		}

		public static string Kick(JObject json, OrderManager orderManager)
		{
			if (!Game.IsHost)
				throw new InvalidOperationException("Only host can kick players");

			var clientIndex = json["clientIndex"]?.ToObject<int>();
			if (clientIndex == null)
				throw new ArgumentException("Missing clientIndex parameter");

			orderManager.IssueOrder(Order.Command($"kick {clientIndex}"));
			return "Kick order issued";
		}

		public static JObject GetLobbyInfo(JObject json, OrderManager orderManager)
		{
			var info = new JObject();
			var lobbyInfo = orderManager.LobbyInfo;

			info["isHost"] = Game.IsHost;
			info["localClientIndex"] = orderManager.LocalClient?.Index;
			
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
					c["color"] = client.Color.ToString();
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
	}
}
