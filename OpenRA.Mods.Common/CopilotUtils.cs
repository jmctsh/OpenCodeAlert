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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Commands;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common
{


	public static class CopilotsUtils
	{
		static readonly HashSet<string> DefenseBuildingTypes = new(StringComparer.OrdinalIgnoreCase) { "gtwr", "gun", "atwr", "obli", "sam" };
		static readonly HashSet<string> RefineryBuildingTypes = new(StringComparer.OrdinalIgnoreCase) { "proc" };

		public static void TryBuild(World world, string buildingName, Actor building, ProductionQueue queue)
		{
			var result = ResolveAutoBuildOrder(world, buildingName, building);
			if (result.Location == null)
				return;

			world.IssueOrder(new Order(result.OrderString, building.Owner.PlayerActor, Target.FromCell(world, result.Location.Value), false)
			{
				// Building to place
				TargetString = buildingName,

				// Actor variant will always be small enough to safely pack in a CPos
				ExtraLocation = new CPos(result.Variant, 0),

				// Actor ID to associate the placement with
				ExtraData = queue.Actor.ActorID,
				SuppressVisualFeedback = true
			});
		}

		// 和 TryBuild 逻辑相同，但通过主线程 Intent 派发放置命令
		public static void TryBuildIntent(World world, string buildingName, Actor building, ProductionQueue queue)
		{
			var result = ResolveAutoBuildOrder(world, buildingName, building);
			if (result.Location == null)
				return;

			OpenRA.Mods.Common.Commands.CopilotBus.Enqueue(new OpenRA.Mods.Common.Commands.IssueOrderIntent
			{
				OrderId = result.OrderString,
				SubjectActorId = (int)building.Owner.PlayerActor.ActorID,
				TargetA = OpenRA.Mods.Common.Commands.TargetSpec.FromCell(result.Location.Value),
				TargetB = OpenRA.Mods.Common.Commands.TargetSpec.None(),
				Queued = false,
				TargetString = buildingName,
				ExtraLocation = new CPos(result.Variant, 0),
				ExtraData = (int)queue.Actor.ActorID,
				SuppressVisualFeedback = true
			});
		}

		static (string OrderString, CPos? Location, int Variant) ResolveAutoBuildOrder(World world, string buildingName, Actor building)
		{
			var player = building.Owner;
			var actorInfo = world.Map.Rules.Actors[buildingName];
			var bi = actorInfo.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return ("PlaceBuilding", null, 0);

			var plugInfo = actorInfo.TraitInfoOrDefault<PlugInfo>();
			if (plugInfo != null)
			{
				var possibleBuilding = world.ActorsWithTrait<Pluggable>().FirstOrDefault(a =>
					a.Actor.Owner == player && a.Trait.AcceptsPlug(plugInfo.Type));

				if (possibleBuilding.Actor != null)
					return ("PlacePlug", possibleBuilding.Actor.Location + possibleBuilding.Trait.Info.Offset, 0);

				return ("PlacePlug", null, 0);
			}

			var type = BuildingType.Building;
			if (DefenseBuildingTypes.Contains(actorInfo.Name))
				type = BuildingType.Defense;
			else if (RefineryBuildingTypes.Contains(actorInfo.Name))
				type = BuildingType.Refinery;

			var placement = ChooseBuildLocation(world, player, actorInfo, bi, true, type);
			return ("PlaceBuilding", placement.Location, placement.Variant);
		}

		static (CPos? Location, int Variant) ChooseBuildLocation(World world, Player player, ActorInfo actorInfo, BuildingInfo bi, bool distanceToBaseIsImportant, BuildingType type)
		{
			const int MinimumDefenseRadius = 0;
			const int MaximumDefenseRadius = 20;
			const int MinBaseRadius = 0;
			const int MaxBaseRadius = 20;
			const int MaxResourceCellsToCheck = 20;

			var resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();
			var baseActors = GetOrderedBaseActors(world, player, type);

			foreach (var baseActor in baseActors)
			{
				var baseCenter = baseActor.Location;

				switch (type)
				{
					case BuildingType.Defense:
					{
						var closestEnemy = world.ActorsHavingTrait<Building>()
							.Where(a => !a.Disposed && player.RelationshipWith(a.Owner) == PlayerRelationship.Enemy)
							.ClosestToIgnoringPath(world.Map.CenterOfCell(baseCenter));

						var targetCell = closestEnemy != null ? closestEnemy.Location : baseCenter;
						var defense = FindPos(world, player, actorInfo, bi, baseCenter, targetCell, MinimumDefenseRadius, MaximumDefenseRadius, true);
						if (defense.Location != null)
							return defense;

						break;
					}

					case BuildingType.Refinery:
						if (resourceLayer != null)
						{
							var nearbyResources = world.Map.FindTilesInAnnulus(baseCenter, MinBaseRadius, MaxBaseRadius)
								.Where(a => resourceLayer.GetResource(a).Type != null)
								.Shuffle(world.LocalRandom)
								.Take(MaxResourceCellsToCheck);

							foreach (var resourceCell in nearbyResources)
							{
								var found = FindPos(world, player, actorInfo, bi, baseCenter, resourceCell, MinBaseRadius, MaxBaseRadius, true);
								if (found.Location != null)
									return found;
							}
						}

						var fallback = FindPos(world, player, actorInfo, bi, baseCenter, baseCenter, MinBaseRadius, MaxBaseRadius, true);
						if (fallback.Location != null)
							return fallback;
						break;

					case BuildingType.Building:
						var buildingLocation = FindPos(world, player, actorInfo, bi, baseCenter, baseCenter, MinBaseRadius,
							distanceToBaseIsImportant ? MaxBaseRadius : world.Map.Grid.MaximumTileSearchRange, distanceToBaseIsImportant);
						if (buildingLocation.Location != null)
							return buildingLocation;
						break;
				}
			}

			return (null, 0);
		}

		static List<Actor> GetOrderedBaseActors(World world, Player player, BuildingType type)
		{
			var baseActors = world.ActorsWithTrait<BaseProvider>()
				.Where(a => a.Actor.Owner == player && a.Trait.Ready())
				.Select(a => a.Actor)
				.ToList();

			if (baseActors.Count == 0)
			{
				var facts = new ActorIndex.OwnerAndNamesAndTrait<Transforms>(world, new List<string> { "fact" }, player);
				baseActors = facts.Actors.ToList();
			}

			if (type == BuildingType.Refinery)
				return baseActors.OrderByDescending(a => a.ActorID).ToList();

			return baseActors.Shuffle(world.LocalRandom).ToList();
		}

		static (CPos? Location, int Variant) FindPos(World world, Player player, ActorInfo actorInfo, BuildingInfo bi,
			CPos center, CPos target, int minRange, int maxRange, bool distanceToBaseIsImportant)
		{
			var actorVariant = 0;
			var buildingVariantInfo = actorInfo.TraitInfoOrDefault<PlaceBuildingVariantsInfo>();
			var variantActorInfo = actorInfo;
			var variantBuildingInfo = bi;
			var cells = world.Map.FindTilesInAnnulus(center, minRange, maxRange);

			if (center != target)
			{
				cells = cells.OrderBy(c => (c - target).LengthSquared);

				if (buildingVariantInfo?.Actors != null)
				{
					if (buildingVariantInfo.Facings != null)
					{
						var vector = world.Map.CenterOfCell(target) - world.Map.CenterOfCell(center);

						// The rotation Y point to upside vertically, so -Y = Y(rotation)
						var desireFacing = new WAngle(WAngle.ArcSin((int)((long)Math.Abs(vector.X) * 1024 / vector.Length)).Angle);
						if (vector.X > 0 && vector.Y >= 0)
							desireFacing = new WAngle(512) - desireFacing;
						else if (vector.X < 0 && vector.Y >= 0)
							desireFacing = new WAngle(512) + desireFacing;
						else if (vector.X < 0 && vector.Y < 0)
							desireFacing = -desireFacing;

						for (int i = 0, e = 1024; i < buildingVariantInfo.Facings.Length; i++)
						{
							var minDelta = Math.Min((desireFacing - buildingVariantInfo.Facings[i]).Angle, (buildingVariantInfo.Facings[i] - desireFacing).Angle);
							if (e > minDelta)
							{
								e = minDelta;
								actorVariant = i;
							}
						}
					}
					else
						actorVariant = world.LocalRandom.Next(buildingVariantInfo.Actors.Length + 1);
				}
			}
			else
			{
				cells = cells.Shuffle(world.LocalRandom);

				if (buildingVariantInfo?.Actors != null)
					actorVariant = world.LocalRandom.Next(buildingVariantInfo.Actors.Length + 1);
			}

			if (actorVariant != 0)
			{
				variantActorInfo = world.Map.Rules.Actors[buildingVariantInfo.Actors[actorVariant - 1]];
				variantBuildingInfo = variantActorInfo.TraitInfoOrDefault<BuildingInfo>();
			}

			foreach (var cell in cells)
			{
				if (!world.CanPlaceBuilding(cell, variantActorInfo, variantBuildingInfo, null))
					continue;

				if (distanceToBaseIsImportant && !variantBuildingInfo.IsCloseEnoughToBase(world, player, variantActorInfo, cell))
					continue;

				return (cell, actorVariant);
			}

			return (null, 0);
		}

		public static bool IsVisibleInViewport(WorldRenderer worldRenderer, WPos position)
		{
			var viewport = worldRenderer.Viewport;
			var topLeft = worldRenderer.ProjectedPosition(viewport.TopLeft);
			var bottomRight = worldRenderer.ProjectedPosition(viewport.BottomRight);

			// 检查世界坐标是否在视窗范围内
			return position.X >= topLeft.X && position.X <= bottomRight.X &&
				   position.Y >= topLeft.Y && position.Y <= bottomRight.Y;
		}

		public static CVec GetDirectionVector(string direction)
		{
			if (direction.EndsWith('方') || direction.EndsWith('侧') || direction.EndsWith('边'))
				direction = direction[..^1];
			switch (direction)
			{
				case "北":
				case "上": return new CVec(0, -1);  // North
				case "右上":
				case "东北": return new CVec(1, -1);  // Northeast
				case "东":
				case "右": return new CVec(1, 0);   // East
				case "右下":
				case "东南": return new CVec(1, 1);   // Southeast
				case "南":
				case "下": return new CVec(0, 1);   // South
				case "左下":
				case "西南": return new CVec(-1, 1);  // Southwest
				case "西":
				case "左": return new CVec(-1, 0);  // West
				case "左上":
				case "西北": return new CVec(-1, -1); // Northwest
				case "任意":
				case "左右":
				case "上下":
				case "附近":
				case "旁":
					return GetRandomDirection(); // Any random direction
				default:
					throw new ArgumentException($"Invalid direction: {direction}");
			}
		}

		static CVec GetRandomDirection()
		{
			var random = new Random();
			var randomDirection = random.Next(8); // Randomly choose from 0 to 7
			switch (randomDirection)
			{
				case 0: return new CVec(0, -1);  // North
				case 1: return new CVec(1, -1);  // Northeast
				case 2: return new CVec(1, 0);   // East
				case 3: return new CVec(1, 1);   // Southeast
				case 4: return new CVec(0, 1);   // South
				case 5: return new CVec(-1, 1);  // Southwest
				case 6: return new CVec(-1, 0);  // West
				case 7: return new CVec(-1, -1); // Northwest
				default:
					throw new InvalidOperationException("Random direction generation failed");
			}
		}

		public static Func<CPos, int> GetCustomMethod(CPos src, CPos dest, string method)
		{
			var value = 0;
			if (method.Contains('左') || method.Contains("left") || method.Contains("Left"))
			{
				value = -1;
			}
			else if (method.Contains('右') || method.Contains("right") || method.Contains("Right"))
			{
				value = 1;
			}

			if (value == 0)
				return null;

			var vectorX = dest.X - src.X;
			var vectorY = dest.Y - src.Y;

			return pos =>
			{
				var posVectorX = pos.X - src.X;
				var posVectorY = pos.Y - src.Y;

				var crossProduct = vectorX * posVectorY - vectorY * posVectorX;
				var distance = crossProduct / Math.Sqrt(vectorX * vectorX + vectorY * vectorY);

				return Math.Max((int)(value * distance * 20 + 100), 0);
			};
		}

		public static void WaitInit()
		{
			waitIndexGen = 0;
			waitStatusMap = new Dictionary<int, string>();
			produceWaitMap = new Dictionary<int, Dictionary<string, int>>();
		}

		public static string QueryWaitStatus(int waitId)
		{
			if (waitStatusMap.ContainsKey(waitId))
				return waitStatusMap[waitId];
			return "Invalid waitId";
		}

		static int waitIndexGen = 0;

		static Dictionary<int, string> waitStatusMap;
		static Dictionary<int, Dictionary<string, int>> produceWaitMap;

		public static int AddWaitEvent_Produce(Dictionary<string, int> produceMap)
		{
			waitIndexGen++;
			var waitIndex = waitIndexGen;
			produceWaitMap.Add(waitIndex, produceMap);
			waitStatusMap.Add(waitIndex, "waiting");
			return waitIndex;
		}

		public static void FinishProduce(string unitName)
		{
			var completedIndexes = new List<int>();

			foreach (var entry in produceWaitMap)
			{
				var waitIndex = entry.Key;
				var produceMap = entry.Value;

				if (produceMap.ContainsKey(unitName) && produceMap[unitName] > 0)
				{
					produceMap[unitName]--;

					if (produceMap[unitName] == 0)
					{
						// Check if all values are now 0
						var allProduced = true;
						foreach (var value in produceMap.Values)
						{
							if (value > 0)
							{
								allProduced = false;
								break;
							}
						}

						if (allProduced)
						{
							completedIndexes.Add(waitIndex);
						}
					}

					break;
				}
			}

			foreach (var index in completedIndexes)
			{
				produceWaitMap.Remove(index);
				waitStatusMap[index] = "success";
			}
		}

		public static string GetFactionRelation(Player player, Actor actor)
		{
			if (actor.Owner == player)
				return "己方";

			if (actor.Owner != null)
			{
				var stance = player.RelationshipWith(actor.Owner);
				if (stance == PlayerRelationship.Enemy)
					return "敌方";
				if (stance == PlayerRelationship.Neutral)
					return "中立";
				if (stance == PlayerRelationship.Ally)
					return "友方";
			}

			return "中立"; // 默认兜底
		}
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Attach this to the world actor.")]
	public class CopilotsTriggersInfo : TraitInfo<CopilotsTriggers> { }

	public class CopilotsTriggers : INotifyProduction
	{
		public void UnitProduced(Actor self, Actor other, CPos exit)
		{
			if (other.Owner == other.World.LocalPlayer)
				CopilotsUtils.FinishProduce(other.Info.Name);
		}
	}
}
