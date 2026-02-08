#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using OpenRA.Network;

namespace OpenRA.Server
{
	sealed class Program
	{
		static void Main(string[] args)
		{
			try
			{
				Run(args);
			}
			finally
			{
				Log.Dispose();
			}
		}

		static void Run(string[] args)
		{
			var normalizedArgs = new List<string>();
			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];
				if (string.IsNullOrWhiteSpace(arg))
					continue;

				if (arg == "--port" || arg == "-p")
				{
					if (i + 1 < args.Length)
						normalizedArgs.Add($"Server.ListenPort={args[++i]}");
					continue;
				}

				if (arg.StartsWith("--port=", StringComparison.Ordinal))
				{
					normalizedArgs.Add($"Server.ListenPort={arg[7..]}");
					continue;
				}

				if (arg == "--listen" || arg == "--listen-address" || arg == "--bind")
				{
					if (i + 1 < args.Length)
						normalizedArgs.Add($"Server.ListenAddress={args[++i]}");
					continue;
				}

				if (arg.StartsWith("--listen=", StringComparison.Ordinal))
				{
					normalizedArgs.Add($"Server.ListenAddress={arg[9..]}");
					continue;
				}

				if (arg == "--lan" || arg == "--lanplay")
				{
					normalizedArgs.Add("Server.LANPlay=True");
					continue;
				}

				if (arg == "--mod" || arg == "-m")
				{
					if (i + 1 < args.Length)
						normalizedArgs.Add($"Game.Mod={args[++i]}");
					continue;
				}

				if (arg.StartsWith("--mod=", StringComparison.Ordinal))
				{
					normalizedArgs.Add($"Game.Mod={arg[6..]}");
					continue;
				}

				normalizedArgs.Add(arg);
			}

			var arguments = new Arguments(normalizedArgs.ToArray());

			var engineDirArg = arguments.GetValue("Engine.EngineDir", null);
			if (!string.IsNullOrEmpty(engineDirArg))
				Platform.OverrideEngineDir(engineDirArg);
			else
			{
				var binDir = Platform.BinDir;
				var binMods = Path.Combine(binDir, "mods");
				var parentMods = Path.Combine(binDir, "..", "mods");
				if (!Directory.Exists(binMods) && Directory.Exists(parentMods))
					Platform.OverrideEngineDir(Path.GetFullPath(Path.Combine(binDir, "..")));
			}

			var supportDirArg = arguments.GetValue("Engine.SupportDir", null);
			if (!string.IsNullOrEmpty(supportDirArg))
				Platform.OverrideSupportDir(supportDirArg);

			Log.AddChannel("debug", "dedicated-debug.log", true);
			Log.AddChannel("perf", "dedicated-perf.log", true);
			Log.AddChannel("server", "dedicated-server.log", true);
			Log.AddChannel("nat", "dedicated-nat.log", true);
			Log.AddChannel("geoip", "dedicated-geoip.log", true);

			var modID = arguments.GetValue("Game.Mod", "copilot");
			var explicitModPaths = new List<string>();
			if (modID != null && (File.Exists(modID) || Directory.Exists(modID)))
			{
				explicitModPaths.Add(modID);
				modID = Path.GetFileNameWithoutExtension(modID);
			}

			if (modID == "copilot")
			{
				var copilotPath = Path.Combine(Platform.EngineDir, "mods", "copilot");
				if (Directory.Exists(copilotPath))
				{
					explicitModPaths.Add(copilotPath);
				}
			}

			if (modID == null)
				throw new InvalidOperationException("Game.Mod argument missing or mod could not be found.");

			if (arguments.Contains("Server.Port"))
				arguments.ReplaceValue("Server.ListenPort", arguments.GetValue("Server.Port", null));

			if (arguments.Contains("Server.LANPlay") && !arguments.Contains("Server.AdvertiseOnline"))
			{
				var lanPlay = arguments.GetValue("Server.LANPlay", null);
				if (bool.TryParse(lanPlay, out var lanEnabled))
					arguments.ReplaceValue("Server.AdvertiseOnline", (!lanEnabled).ToString());
			}

			// HACK: The engine code assumes that Game.Settings is set.
			// This isn't nearly as bad as ModData, but is still not very nice.
			Game.InitializeSettings(arguments);
			var settings = Game.Settings.Server;
			var listenAddressArg = arguments.GetValue("Server.ListenAddress", null);

			Nat.Initialize();

			var envModSearchPaths = Environment.GetEnvironmentVariable("MOD_SEARCH_PATHS");
			var modSearchPaths = !string.IsNullOrWhiteSpace(envModSearchPaths) ?
				FieldLoader.GetValue<string[]>("MOD_SEARCH_PATHS", envModSearchPaths) :
				new[] { Path.Combine(Platform.EngineDir, "mods") };

			var mods = new InstalledMods(modSearchPaths, explicitModPaths);

			WriteLineWithTimeStamp($"Starting dedicated server for mod: {modID}");
			while (true)
			{
				// HACK: The engine code *still* assumes that Game.ModData is set
				var modData = Game.ModData = new ModData(mods[modID], mods);
				modData.MapCache.LoadPreviewImages = false; // PERF: Server doesn't need previews, save memory by not loading them.
				modData.MapCache.LoadMaps();

				// HACK: Related to the above one, initialize the translations so we can load maps with their (translated) lobby options.
				TranslationProvider.Initialize(modData, modData.DefaultFileSystem);

				var endpoints = new List<IPEndPoint>();
				if (!string.IsNullOrWhiteSpace(listenAddressArg))
				{
					var addresses = new List<IPAddress>();
					var parts = listenAddressArg.Split(',', StringSplitOptions.RemoveEmptyEntries);
					foreach (var part in parts)
					{
						var token = part.Trim();
						if (token.Length == 0)
							continue;

						if (token == "0.0.0.0")
							addresses.Add(IPAddress.Any);
						else if (token == "::" || token == "[::]")
							addresses.Add(IPAddress.IPv6Any);
						else if (IPAddress.TryParse(token, out var ip))
							addresses.Add(ip);
						else
						{
							try
							{
								addresses.AddRange(Dns.GetHostAddresses(token));
							}
							catch { }
						}
					}

					if (addresses.Count == 0)
						throw new InvalidOperationException("Server.ListenAddress argument is invalid.");

					foreach (var address in addresses)
						endpoints.Add(new IPEndPoint(address, settings.ListenPort));
				}
				else
				{
					endpoints.Add(new IPEndPoint(IPAddress.IPv6Any, settings.ListenPort));
					endpoints.Add(new IPEndPoint(IPAddress.Any, settings.ListenPort));
				}
				var server = new Server(endpoints, settings, modData, ServerType.Dedicated);

				GC.Collect();
				while (true)
				{
					Thread.Sleep(1000);
					if (server.State == ServerState.GameStarted && server.Conns.Count < 1)
					{
						WriteLineWithTimeStamp("No one is playing, shutting down...");
						server.Shutdown();
						break;
					}
				}

				modData.Dispose();
				WriteLineWithTimeStamp("Starting a new server instance...");
			}
		}

		static void WriteLineWithTimeStamp(string line)
		{
			Console.WriteLine($"[{DateTime.Now.ToString(Game.Settings.Server.TimestampFormat, CultureInfo.CurrentCulture)}] {line}");
		}
	}
}
