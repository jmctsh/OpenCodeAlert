using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Commands;

namespace OpenRA.Mods.Common.Traits
{
    [TraitLocation(SystemActors.Player)]
    public class CopilotExpansionManagerInfo : TraitInfo<CopilotExpansionManager> { }

    public class CopilotExpansionManager : ITick, IWorldLoaded
    {
        World world;
        Player player;
        IResourceLayer resourceLayer;

        enum ExpansionState
        {
            Idle,
            BuildingMCV,
            WaitingForMCV,
            MovingMCV,
            DeployingMCV,
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
        const int TargetRefineryCount = 3;
        
        Actor mcvActor;
        Actor newBaseActor;
        CPos deployLocation;
        
        // Ticks to wait before retrying actions
        int waitTicks = 0;

        // Configurable names (could be moved to Info)
        readonly string[] mcvNames = { "mcv" };
        readonly string[] powerNames = { "powr", "apwr" };
        readonly string[] refineryNames = { "proc" };

        public void WorldLoaded(World w, WorldRenderer wr)
        {
            world = w;
        }

        public void StartExpansion(Player p)
        {
            if (currentState != ExpansionState.Idle)
                return;

            player = p;
            resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();
            currentState = ExpansionState.BuildingMCV;
            refineryCount = 0;
            mcvActor = null;
            newBaseActor = null;
        }

        public void Tick(Actor self)
        {
            if (currentState == ExpansionState.Idle || --waitTicks > 0)
                return;

            switch (currentState)
            {
                case ExpansionState.BuildingMCV:
                    if (!StartProduction(mcvNames))
                    {
                        waitTicks = 50; // Retry later
                        return;
                    }
                    currentState = ExpansionState.WaitingForMCV;
                    break;

                case ExpansionState.WaitingForMCV:
                    mcvActor = FindIdleMCV();
                    if (mcvActor != null)
                    {
                        currentState = ExpansionState.MovingMCV;
                    }
                    else
                    {
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.MovingMCV:
                    if (deployLocation == CPos.Zero)
                    {
                        deployLocation = ChooseExpansionLocation();
                        if (deployLocation == CPos.Zero)
                        {
                            // Failed to find location, abort or fallback
                            // Fallback to random location near current base?
                            // For now, just abort
                            currentState = ExpansionState.Idle;
                            return;
                        }
                    }

                    if (mcvActor.Location == deployLocation)
                    {
                        currentState = ExpansionState.DeployingMCV;
                    }
                    else
                    {
                        // Keep ordering move until arrived
                        world.IssueOrder(new Order("Move", mcvActor, Target.FromCell(world, deployLocation), false));
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.DeployingMCV:
                    if (mcvActor.IsDead)
                    {
                        currentState = ExpansionState.Idle; // MCV died
                        return;
                    }
                    
                    world.IssueOrder(new Order("DeployTransform", mcvActor, false));
                    currentState = ExpansionState.WaitingForBase;
                    waitTicks = 50; // Give time to transform
                    break;

                case ExpansionState.WaitingForBase:
                    // Find the new base building at the deploy location
                    newBaseActor = world.ActorMap.GetActorsAt(deployLocation)
                        .FirstOrDefault(a => a.Owner == player && a.Info.HasTraitInfo<BuildingInfo>());
                    
                    if (newBaseActor != null)
                    {
                        currentState = ExpansionState.BuildingPower;
                    }
                    else
                    {
                        waitTicks = 25;
                    }
                    break;

                case ExpansionState.BuildingPower:
                    if (!StartProduction(powerNames))
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

                    if (!StartProduction(refineryNames))
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

        bool StartProduction(string[] types)
        {
            var queue = FindQueueFor(types);
            if (queue == null) return false;

            var item = types.FirstOrDefault(t => world.Map.Rules.Actors.ContainsKey(t));
            if (item == null) return false;

            // Check if already producing
            if (queue.AllQueued().Any(i => types.Contains(i.Item)))
                return true;

            world.IssueOrder(Order.StartProduction(queue.Actor, item, 1));
            return true;
        }

        bool IsItemReady(string[] types)
        {
            var queue = FindQueueFor(types);
            if (queue == null) return false;
            return queue.AllQueued().Any(i => types.Contains(i.Item) && i.Done);
        }

        ProductionQueue FindQueueFor(string[] types)
        {
            var queues = world.ActorsWithTrait<ProductionQueue>()
                .Where(a => a.Actor.Owner == player)
                .Select(a => a.Trait);

            foreach (var q in queues)
            {
                var buildable = q.BuildableItems();
                if (buildable.Any(b => types.Contains(b.Name)))
                    return q;
            }
            return null;
        }

        Actor FindIdleMCV()
        {
            return world.ActorsHavingTrait<Transforms>()
                .FirstOrDefault(a => a.Owner == player && a.IsIdle && mcvNames.Contains(a.Info.Name));
        }

        bool PlaceBuilding(string[] types, CPos near)
        {
            var queue = FindQueueFor(types);
            if (queue == null) return false;

            var item = queue.AllQueued().FirstOrDefault(i => types.Contains(i.Item) && i.Done);
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

        CPos ChooseExpansionLocation()
        {
            if (resourceLayer == null) return CPos.Zero;

            var baseCenter = player.PlayerActor.Location; // Or average of bases
            var bases = world.ActorsHavingTrait<BaseProvider>().Where(a => a.Owner == player).ToList();
            if (bases.Any())
                baseCenter = bases.First().Location;

            var potentialTiles = world.Map.FindTilesInAnnulus(baseCenter, 30, 50)
                .Where(c => resourceLayer.GetResource(c).Type != null)
                .Shuffle(world.LocalRandom)
                .Take(20);

            var mcvInfo = world.Map.Rules.Actors[mcvNames[0]];
            var transforms = mcvInfo.TraitInfo<TransformsInfo>();
            var buildingInfo = world.Map.Rules.Actors[transforms.IntoActor].TraitInfo<BuildingInfo>();

            foreach (var tile in potentialTiles)
            {
                // Try to find a buildable spot near the resource
                // Increase search radius to improve chances of finding a valid spot
                foreach (var cell in world.Map.FindTilesInAnnulus(tile, 2, 10))
                {
                    // Ensure the location is not too close to existing bases to encourage actual expansion
                    if (bases.Any(b => (b.Location - cell).LengthSquared < 30 * 30))
                        continue;

                    if (world.CanPlaceBuilding(cell, world.Map.Rules.Actors[transforms.IntoActor], buildingInfo, null))
                    {
                        return cell;
                    }
                }
            }

            return CPos.Zero;
        }
    }
}
