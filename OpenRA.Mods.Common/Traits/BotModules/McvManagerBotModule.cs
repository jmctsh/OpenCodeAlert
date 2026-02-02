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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Manages AI MCVs.")]
	public class McvManagerBotModuleInfo : ConditionalTraitInfo
	{
		[Desc("Actor types that are considered MCVs (deploy into base builders).")]
		public readonly HashSet<string> McvTypes = new();

		[Desc("Actor types that are considered construction yards (base builders).")]
		public readonly HashSet<string> ConstructionYardTypes = new();

		[Desc("Actor types that are able to produce MCVs.")]
		public readonly HashSet<string> McvFactoryTypes = new();

		[Desc("Try to maintain at least this many ConstructionYardTypes, build an MCV if number is below this.")]
		public readonly int MinimumConstructionYardCount = 1;

		[Desc("Delay (in ticks) between looking for and giving out orders to new MCVs.")]
		public readonly int ScanForNewMcvInterval = 20;

		[Desc("Minimum distance in cells from center of the base when checking for MCV deployment location.")]
		public readonly int MinBaseRadius = 2;

		[Desc("Maximum distance in cells from center of the base when checking for MCV deployment location.",
			"Only applies if RestrictMCVDeploymentFallbackToBase is enabled and there's at least one construction yard.")]
		public readonly int MaxBaseRadius = 20;

		[Desc("Should deployment of additional MCVs be restricted to MaxBaseRadius if explicit deploy locations are missing or occupied?")]
		public readonly bool RestrictMCVDeploymentFallbackToBase = true;

		[Desc("Cash threshold to trigger expansion (building a new MCV).")]
		public readonly int ExpansionCashThreshold = 1000;

		[Desc("Maximum number of bases (construction yards) allowed.")]
		public readonly int MaxBaseCount = 2;

		[Desc("Minimum distance from existing bases for a new expansion.")]
		public readonly int MinimumExpansionDistance = 30;

		[Desc("Maximum distance from existing bases for a new expansion.")]
		public readonly int MaximumExpansionDistance = 50;

		public override object Create(ActorInitializer init) { return new McvManagerBotModule(init.Self, this); }
	}

	public class McvManagerBotModule : ConditionalTrait<McvManagerBotModuleInfo>,
		IBotTick, IBotPositionsUpdated, IGameSaveTraitData, INotifyActorDisposing
	{
		public CPos GetRandomBaseCenter()
		{
			var randomConstructionYard = constructionYards.Actors
				.RandomOrDefault(world.LocalRandom);

			return randomConstructionYard?.Location ?? initialBaseCenter;
		}

		readonly World world;
		readonly Player player;
		readonly ActorIndex.OwnerAndNamesAndTrait<Transforms> mcvs;
		readonly ActorIndex.OwnerAndNamesAndTrait<Building> constructionYards;
		readonly ActorIndex.OwnerAndNamesAndTrait<Building> mcvFactories;

		IBotPositionsUpdated[] notifyPositionsUpdated;
		IBotRequestUnitProduction[] requestUnitProduction;
		PlayerResources playerResources;
		IResourceLayer resourceLayer;
		TechTree techTree;

		CPos initialBaseCenter;
		int scanInterval;
		bool firstTick = true;

		public McvManagerBotModule(Actor self, McvManagerBotModuleInfo info)
			: base(info)
		{
			world = self.World;
			player = self.Owner;
			mcvs = new ActorIndex.OwnerAndNamesAndTrait<Transforms>(world, info.McvTypes, player);
			constructionYards = new ActorIndex.OwnerAndNamesAndTrait<Building>(world, info.ConstructionYardTypes, player);
			mcvFactories = new ActorIndex.OwnerAndNamesAndTrait<Building>(world, info.McvFactoryTypes, player);
		}

		protected override void Created(Actor self)
		{
			notifyPositionsUpdated = self.Owner.PlayerActor.TraitsImplementing<IBotPositionsUpdated>().ToArray();
			requestUnitProduction = self.Owner.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>().ToArray();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
			resourceLayer = self.World.WorldActor.TraitOrDefault<IResourceLayer>();
			techTree = self.Owner.PlayerActor.TraitOrDefault<TechTree>();
		}

		protected override void TraitEnabled(Actor self)
		{
			// Avoid all AIs reevaluating assignments on the same tick, randomize their initial evaluation delay.
			scanInterval = world.LocalRandom.Next(Info.ScanForNewMcvInterval, Info.ScanForNewMcvInterval * 2);
		}

		void IBotPositionsUpdated.UpdatedBaseCenter(CPos newLocation)
		{
			initialBaseCenter = newLocation;
		}

		void IBotPositionsUpdated.UpdatedDefenseCenter(CPos newLocation) { }

		void IBotTick.BotTick(IBot bot)
		{
			if (firstTick)
			{
				DeployMcvs(bot, false);
				firstTick = false;
			}

			if (--scanInterval <= 0)
			{
				scanInterval = Info.ScanForNewMcvInterval;
				DeployMcvs(bot, true);

				// No construction yards - Build a new MCV
				var unitBuilder = requestUnitProduction.FirstEnabledTraitOrDefault();
				if (unitBuilder != null && Info.McvTypes.Count > 0 && ShouldBuildMCV())
				{
					var mcvType = Info.McvTypes.Random(world.LocalRandom);
					if (unitBuilder.RequestedProductionCount(bot, mcvType) == 0)
						unitBuilder.RequestUnitProduction(bot, mcvType);
				}
			}
		}

		bool ShouldBuildMCV()
		{
			// Only build MCV if we don't already have one in the field.
			var allowedToBuildMCV = AIUtils.CountActorByCommonName(mcvs) == 0;
			if (!allowedToBuildMCV)
				return false;

			var constructionYardCount = AIUtils.CountActorByCommonName(constructionYards);

			// Recovery Mode: Build MCV if we don't have any construction yards (and we have a factory to build it).
			if (constructionYardCount < Info.MinimumConstructionYardCount && AIUtils.CountActorByCommonName(mcvFactories) > 0)
				return true;

			// Expansion Mode: Build MCV if we have excess cash and haven't reached max base count.
			var activeMcvs = AIUtils.CountActorByCommonName(mcvs);

			// Count MCVs currently in production queue (to avoid double ordering)
			var mcvsInProduction = world.ActorsWithTrait<ProductionQueue>()
				.Where(a => a.Actor.Owner == player)
				.SelectMany(a => a.Trait.AllQueued())
				.Count(i => Info.McvTypes.Contains(i.Item));

			if (constructionYardCount + activeMcvs + mcvsInProduction < Info.MaxBaseCount)
			{
				// Check if we can build any MCV (prerequisites met)
				foreach (var mcvType in Info.McvTypes)
				{
					if (!world.Map.Rules.Actors.TryGetValue(mcvType, out var actorInfo))
						continue;

					var buildable = actorInfo.TraitInfoOrDefault<BuildableInfo>();
					if (buildable != null && techTree != null && techTree.HasPrerequisites(buildable.Prerequisites))
						return true;
				}
			}

			return false;
		}

		void DeployMcvs(IBot bot, bool chooseLocation)
		{
			var newMCVs = mcvs.Actors
				.Where(a => a.IsIdle);

			foreach (var mcv in newMCVs)
				DeployMcv(bot, mcv, chooseLocation);
		}

		// Find any MCV and deploy them at a sensible location.
		void DeployMcv(IBot bot, Actor mcv, bool move)
		{
			if (move)
			{
				// If we lack a base, we need to make sure we don't restrict deployment of the MCV to the base!
				var baseCount = AIUtils.CountActorByCommonName(constructionYards);
				var restrictToBase =
					Info.RestrictMCVDeploymentFallbackToBase &&
					baseCount > 0;

				// If we are expanding (have at least one base), we should look for a spot further away
				var isExpansion = baseCount > 0;

				var transformsInfo = mcv.Info.TraitInfo<TransformsInfo>();
				var desiredLocation = ChooseMcvDeployLocation(transformsInfo.IntoActor, transformsInfo.Offset, restrictToBase, isExpansion);
				if (desiredLocation == null)
					return;

				bot.QueueOrder(new Order("Move", mcv, Target.FromCell(world, desiredLocation.Value), true));
			}

			// If the MCV has to move first, we can't be sure it reaches the destination alive, so we only
			// update base and defense center if the MCV is deployed immediately (i.e. at game start).
			// TODO: This could be addressed via INotifyTransform.
			foreach (var n in notifyPositionsUpdated)
			{
				n.UpdatedBaseCenter(mcv.Location);
				n.UpdatedDefenseCenter(mcv.Location);
			}

			bot.QueueOrder(new Order("DeployTransform", mcv, true));
		}

		CPos? ChooseMcvDeployLocation(string actorType, CVec offset, bool distanceToBaseIsImportant, bool isExpansion)
		{
			var actorInfo = world.Map.Rules.Actors[actorType];
			var bi = actorInfo.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return null;

			// Find the buildable cell that is closest to pos and centered around center
			CPos? FindPos(CPos center, CPos target, int minRange, int maxRange)
			{
				var cells = world.Map.FindTilesInAnnulus(center, minRange, maxRange);

				// Sort by distance to target if we have one
				if (center != target)
					cells = cells.OrderBy(c => (c - target).LengthSquared);
				else
					cells = cells.Shuffle(world.LocalRandom);

				foreach (var cell in cells)
					if (world.CanPlaceBuilding(cell + offset, actorInfo, bi, null))
						return cell;

				return null;
			}

			var baseCenter = GetRandomBaseCenter();
			var targetCenter = baseCenter;
			var minRange = Info.MinBaseRadius;
			var maxRange = distanceToBaseIsImportant ? Info.MaxBaseRadius : world.Map.Grid.MaximumTileSearchRange;

			if (isExpansion && resourceLayer != null)
			{
				// Find a resource patch that is far enough from existing bases
				var existingBases = constructionYards.Actors.Select(a => a.Location).ToList();
				var maxSearchRadius = System.Math.Min(Info.MaximumExpansionDistance, world.Map.Grid.MaximumTileSearchRange);
				var minSearchRadius = System.Math.Min(Info.MinimumExpansionDistance, maxSearchRadius);

				var potentialResourceTiles = world.Map.FindTilesInAnnulus(baseCenter, minSearchRadius, maxSearchRadius)
					.Where(c => resourceLayer.GetResource(c).Type != null)
					.Shuffle(world.LocalRandom)
					.Take(20);

				foreach (var tile in potentialResourceTiles)
				{
					// Ensure this tile is far from ALL existing bases
					if (existingBases.All(baseLoc => (tile - baseLoc).LengthSquared >= Info.MinimumExpansionDistance * Info.MinimumExpansionDistance))
					{
						targetCenter = tile;
						minRange = 2; // Close to resources
						maxRange = 10;
						break;
					}
				}
			}

			return FindPos(targetCenter, targetCenter, minRange, maxRange);
		}

		List<MiniYamlNode> IGameSaveTraitData.IssueTraitData(Actor self)
		{
			if (IsTraitDisabled)
				return null;

			return new List<MiniYamlNode>()
			{
				new("InitialBaseCenter", FieldSaver.FormatValue(initialBaseCenter))
			};
		}

		void IGameSaveTraitData.ResolveTraitData(Actor self, MiniYaml data)
		{
			if (self.World.IsReplay)
				return;

			var initialBaseCenterNode = data.NodeWithKeyOrDefault("InitialBaseCenter");
			if (initialBaseCenterNode != null)
				initialBaseCenter = FieldLoader.GetValue<CPos>("InitialBaseCenter", initialBaseCenterNode.Value.Value);
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			mcvs.Dispose();
			constructionYards.Dispose();
			mcvFactories.Dispose();
		}
	}
}
