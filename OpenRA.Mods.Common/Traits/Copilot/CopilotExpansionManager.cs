using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Commands;

namespace OpenRA.Mods.Common.Traits
{
    [TraitLocation(SystemActors.Player)]
    public class CopilotExpansionManagerInfo : TraitInfo
    {
        [Desc("Minimum distance in cells from center of the base when checking for MCV deployment location.")]
        public readonly int MinBaseRadius = 2;

        [Desc("Maximum distance in cells from center of the base when checking for MCV deployment location.",
            "Only applies if RestrictMCVDeploymentFallbackToBase is enabled and there's at least one construction yard.")]
        public readonly int MaxBaseRadius = 20;

        [Desc("Should deployment of additional MCVs be restricted to MaxBaseRadius if explicit deploy locations are missing or occupied?")]
        public readonly bool RestrictMCVDeploymentFallbackToBase = true;

        [Desc("Minimum distance from existing bases for a new expansion.")]
        public readonly int MinimumExpansionDistance = 30;

        [Desc("Maximum distance from existing bases for a new expansion.")]
        public readonly int MaximumExpansionDistance = 50;

        public override object Create(ActorInitializer init) { return new CopilotExpansionManager(init.Self, this); }
    }

    public class CopilotExpansionManager : ITick, IWorldLoaded
    {
        public readonly CopilotExpansionManagerInfo Info;
        World world;
        Player player;
        IResourceLayer resourceLayer;
        Dictionary<string, string> actorNameLookup;

        enum ExpansionState
        {
            Idle,
            WaitingForMCV,
            MovingMCV,
            WaitingForBase,
            BuildingPower,
            WaitingForPower,
            PlacingPower,
            BuildingRefinery,
            WaitingForRefinery,
            PlacingRefinery
        }

        ExpansionState currentState = ExpansionState.Idle;
        int refineryCount = 0;
        const int TargetRefineryCount = 2;
        
        Actor mcvActor;
        Actor newBaseActor;
        CPos deployLocation;
        
        // Ticks to wait before retrying actions
        int waitTicks = 0;

        // Configurable names (could be moved to Info)
        readonly string[] mcvNames = { "MCV" };
        readonly string[] powerNames = { "POWR", "APWR", "PWR", "NUKR" };
        readonly string[] refineryNames = { "PROC" };

        CVec deployOffset = CVec.Zero;

        public CopilotExpansionManager(Actor self, CopilotExpansionManagerInfo info)
        {
            Info = info;
        }

        public void WorldLoaded(World w, WorldRenderer wr)
        {
            world = w;
            actorNameLookup = world.Map.Rules.Actors.Keys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k, k => k, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryStartExpansion(Player p, out string message)
        {
            if (world == null)
            {
                message = "World not initialized for expansion.";
                return false;
            }

            if (p == null)
            {
                message = "Invalid player.";
                return false;
            }

            if (currentState != ExpansionState.Idle)
            {
                message = "Base expansion already in progress.";
                return false;
            }

            player = p;
            resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();
            deployOffset = CVec.Zero;

            // Check for existing idle MCV first
            var idleMcv = FindIdleMCV();
            if (idleMcv != null)
            {
                currentState = ExpansionState.WaitingForMCV;
                message = "已找到可用MCV，准备展开。";
                waitTicks = 10;
                return true;
            }

            // Start MCV production
            if (!EnsureProduction(mcvNames, 1))
            {
                var mcvName = ResolveActorName(mcvNames.FirstOrDefault());
                var missing = DescribeMissingPrerequisites(mcvName);
                message = string.IsNullOrEmpty(missing)
                    ? "无法开始建造MCV（可能是资金不足或缺少工厂）。"
                    : $"无法开始建造MCV，缺少前置条件：{missing}";
                currentState = ExpansionState.Idle;
                return false;
            }

            currentState = ExpansionState.WaitingForMCV;
            message = "已开始建造MCV，完成后将自动扩展基地。";
            waitTicks = 50;
            return true;
        }

        public void Tick(Actor self)
        {
            if (currentState == ExpansionState.Idle || --waitTicks > 0)
                return;

            switch (currentState)
            {
                case ExpansionState.MovingMCV:
                    // Should not be here if we skipped it, but kept for logic structure
                    break;

                case ExpansionState.WaitingForMCV:
                    mcvActor = FindIdleMCV();
                    if (mcvActor != null)
                    {
                        // Found an idle MCV (newly built or existing)
                        // Use the ported AI logic for location selection
                        var constructionYards = world.ActorsHavingTrait<BaseBuilding>()
                            .Where(a => a.Owner == player);
            
                        var baseCount = constructionYards.Count();
                        var restrictToBase = Info.RestrictMCVDeploymentFallbackToBase && baseCount > 0;
                        var isExpansion = baseCount > 0;
            
                        var transformsInfo = mcvActor.Info.TraitInfo<TransformsInfo>();
                        var desiredLocation = ChooseMcvDeployLocation(transformsInfo.IntoActor, transformsInfo.Offset, restrictToBase, isExpansion);
            
                        if (desiredLocation == null)
                        {
                            // Keep waiting if we can't find a spot yet
                            waitTicks = 50;
                            return;
                        }
            
                        deployLocation = desiredLocation.Value;
            
                        // Issue orders immediately to ensure they are sent
                        // 1. Move
                        world.IssueOrder(new Order("Move", mcvActor, Target.FromCell(world, deployLocation), false));
                        // 2. DeployTransform (queued=true)
                        world.IssueOrder(new Order("DeployTransform", mcvActor, true));
            
                        currentState = ExpansionState.WaitingForBase;
                        refineryCount = 0;
                        newBaseActor = null;
                        waitTicks = 100; // Give time for moving and deploying
                    }
                    else
                    {
                        // Still waiting for MCV to be produced
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.WaitingForBase:
                    // Find the new base building at the deploy location
                    // We check radius 4 around the expected location
                    var expectedLocation = deployLocation + deployOffset;
                    newBaseActor = world.ActorsHavingTrait<BaseProvider>()
                            .Where(a => a.Owner == player)
                            .OrderBy(a => (a.Location - expectedLocation).LengthSquared)
                            .FirstOrDefault(a => (a.Location - expectedLocation).LengthSquared <= 16); // slightly increased radius
                    
                    if (newBaseActor != null)
                    {
                        currentState = ExpansionState.BuildingPower;
                    }
                    else
                    {
                        // Keep waiting if MCV is still moving/deploying
                        if (mcvActor != null && !mcvActor.IsDead)
                        {
                             // If MCV is idle but not at location, maybe it got stuck? Resend move?
                             // For now just wait.
                             waitTicks = 25;
                        }
                        else
                        {
                             // MCV is gone (deployed or dead), but no base yet?
                             waitTicks = 25;
                        }
                    }
                    break;

                case ExpansionState.BuildingPower:
                    if (!EnsureProduction(powerNames, 1))
                    {
                        waitTicks = 50;
                        return;
                    }
                    currentState = ExpansionState.WaitingForPower;
                    break;

                case ExpansionState.WaitingForPower:
                    if (IsItemReady(powerNames))
                    {
                        currentState = ExpansionState.PlacingPower;
                    }
                    else
                    {
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.PlacingPower:
                    if (PlaceBuilding(powerNames, newBaseActor.Location))
                    {
                        currentState = ExpansionState.BuildingRefinery;
                    }
                    else
                    {
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.BuildingRefinery:
                    if (refineryCount >= TargetRefineryCount)
                    {
                        currentState = ExpansionState.Idle;
                        return;
                    }

                    // Check if queue has a Done item of this type (meaning previous placement pending)
                    if (IsItemReady(refineryNames))
                    {
                         // Still waiting for previous item to be removed from queue
                         waitTicks = 25;
                         return;
                    }

                    int needed = TargetRefineryCount - refineryCount;
                    if (!EnsureProduction(refineryNames, needed))
                    {
                        waitTicks = 50;
                        return;
                    }
                    currentState = ExpansionState.WaitingForRefinery;
                    break;

                case ExpansionState.WaitingForRefinery:
                    if (IsItemReady(refineryNames))
                    {
                        currentState = ExpansionState.PlacingRefinery;
                    }
                    else
                    {
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.PlacingRefinery:
                    if (PlaceBuilding(refineryNames, newBaseActor.Location))
                    {
                        refineryCount++;
                        currentState = ExpansionState.BuildingRefinery;
                    }
                    else
                    {
                        waitTicks = 25;
                    }
                    break;
            }
        }

        // --- Ported from McvManagerBotModule ---

        CPos GetRandomBaseCenter()
        {
            var constructionYards = world.ActorsHavingTrait<BaseBuilding>()
                .Where(a => a.Owner == player)
                .ToList();
                
            var randomConstructionYard = constructionYards.RandomOrDefault(world.LocalRandom);
            return randomConstructionYard?.Location ?? player.PlayerActor.Location;
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
                var existingBases = world.ActorsHavingTrait<BaseBuilding>()
                    .Where(a => a.Owner == player)
                    .Select(a => a.Location).ToList();

                var maxSearchRadius = Math.Min(Info.MaximumExpansionDistance, world.Map.Grid.MaximumTileSearchRange);
                var minSearchRadius = Math.Min(Info.MinimumExpansionDistance, maxSearchRadius);

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

        // --- Helpers ---

        bool EnsureProduction(string[] types, int quantity)
        {
            var typesSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
            var item = types.Select(ResolveActorName).FirstOrDefault(name => name != null);
            if (item == null)
                return false;

            var queue = FindQueueForActor(item);
            if (queue == null) return false;

            // Check how many are already queued or being built
            var currentQueued = queue.AllQueued().Count(i => typesSet.Contains(i.Item));
            var toBuild = quantity - currentQueued;

            if (toBuild > 0)
            {
                world.IssueOrder(Order.StartProduction(queue.Actor, item, toBuild));
            }
            return true;
        }

        bool IsItemReady(string[] types)
        {
            var queue = FindQueueFor(types);
            if (queue == null) return false;
            var typesSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
            return queue.AllQueued().Any(i => typesSet.Contains(i.Item) && i.Done);
        }

        ProductionQueue FindQueueFor(string[] types)
        {
            var typesSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
            var queues = world.ActorsWithTrait<ProductionQueue>()
                .Where(a => a.Actor.Owner == player)
                .Select(a => a.Trait);

            foreach (var q in queues)
            {
                var buildable = q.BuildableItems();
                if (buildable.Any(b => typesSet.Contains(b.Name)))
                    return q;
            }
            return null;
        }

        Actor FindIdleMCV()
        {
            var typesSet = new HashSet<string>(mcvNames, StringComparer.OrdinalIgnoreCase);
            return world.ActorsHavingTrait<Transforms>()
                .FirstOrDefault(a => a.Owner == player && a.IsIdle && typesSet.Contains(a.Info.Name));
        }

        bool PlaceBuilding(string[] types, CPos near)
        {
            var queue = FindQueueFor(types);
            if (queue == null) return false;

            var typesSet = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
            var item = queue.AllQueued().FirstOrDefault(i => typesSet.Contains(i.Item) && i.Done);
            if (item == null) return false;

            var actorInfo = world.Map.Rules.Actors[item.Item];
            var bi = actorInfo.TraitInfoOrDefault<BuildingInfo>();
            
            // Find location near 'near'
            var location = FindPlaceLocation(near, actorInfo, bi);
            if (location == null) return false;

            world.IssueOrder(new Order("PlaceBuilding", player.PlayerActor, Target.FromCell(world, location.Value), false)
            {
                TargetString = item.Item,
                ExtraLocation = new CPos(0, 0), // Default variant
                ExtraData = queue.Actor.ActorID,
                SuppressVisualFeedback = true
            });

            return true;
        }

        CPos? FindPlaceLocation(CPos center, ActorInfo ai, BuildingInfo bi)
        {
            // Simple spiral search
            for (int r = 2; r < 15; r++)
            {
                foreach (var cell in world.Map.FindTilesInAnnulus(center, r, r))
                {
                    if (world.CanPlaceBuilding(cell, ai, bi, null) && bi.IsCloseEnoughToBase(world, player, ai, cell))
                        return cell;
                }
            }
            return null;
        }

        ProductionQueue FindQueueForActor(string actorName)
        {
            if (world == null || player == null)
                return null;

            if (!world.Map.Rules.Actors.TryGetValue(actorName, out var actorInfo))
                return null;

            var buildable = actorInfo.TraitInfoOrDefault<BuildableInfo>();
            if (buildable == null)
                return null;

            foreach (var queueType in buildable.Queue)
            {
                var queue = AIUtils.FindQueues(player, queueType)
                    .FirstOrDefault(q => q.CanBuild(actorInfo));
                if (queue != null)
                    return queue;
            }

            return null;
        }

        string ResolveActorName(string name)
        {
            if (string.IsNullOrEmpty(name) || actorNameLookup == null)
                return null;

            return actorNameLookup.TryGetValue(name, out var resolved) ? resolved : null;
        }

        string DescribeMissingPrerequisites(string actorName)
        {
            if (string.IsNullOrEmpty(actorName))
                return null;

            if (!world.Map.Rules.Actors.TryGetValue(actorName, out var actorInfo))
                return null;

            var bi = actorInfo.TraitInfoOrDefault<BuildableInfo>();
            if (bi == null || bi.Prerequisites == null || bi.Prerequisites.Length == 0)
                return null;

            var tech = player.PlayerActor.TraitOrDefault<TechTree>();
            if (tech == null)
                return null;

            var needed = bi.Prerequisites
                .Where(p => !p.StartsWith("!"))
                .Select(p => p.Replace("~", ""))
                .Where(p => !tech.HasPrerequisites(new[] { p }))
                .Distinct()
                .ToList();

            if (needed.Count == 0)
                return null;

            return string.Join(", ", needed);
        }
    }
}
