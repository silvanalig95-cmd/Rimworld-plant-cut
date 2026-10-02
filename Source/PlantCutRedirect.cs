using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace BuildersDontCutPlants
{
    /// <summary>
    /// Decides when a builder has to leave a blocking plant to the plant cutters, and manages the
    /// vanilla "cut plant" designations this mod places. Nothing here is saved with the game.
    /// </summary>
    internal static class PlantCutRedirect
    {
        private sealed class MarkInfo
        {
            /// <summary>Designation we replaced (e.g. a harvest order on a tree that isn't harvestable yet), restored on cleanup.</summary>
            public DesignationDef replaced;
        }

        // Cut designations placed by this mod during the current session. Weak keys: entries vanish with the designation.
        private static readonly ConditionalWeakTable<Designation, MarkInfo> ourMarks = new ConditionalWeakTable<Designation, MarkInfo>();

        private static readonly HashSet<int> warnedPlantIds = new HashSet<int>();
        private static readonly Dictionary<int, bool> plantCutterOnMap = new Dictionary<int, bool>();
        private static int plantCutterCacheTick = -1;
        private static Game cachedGame;

        private static BuildersDontCutPlantsSettings Settings => BuildersDontCutPlantsMod.Settings;

        public static bool IsPlayerPawn(Pawn pawn) =>
            pawn != null && pawn.Spawned && pawn.Faction != null && pawn.Faction.IsPlayer;

        /// <summary>Blueprints and frames the player wants built; re-planting an extracted tree is plant work, so it's left alone.</summary>
        public static bool IsConstructionSite(Thing thing) =>
            (thing is Blueprint || thing is Frame)
            && thing.Spawned
            && thing.Faction != null && thing.Faction.IsPlayer
            && !(thing.def.entityDefToBuild is ThingDef { plant: not null });

        /// <summary>Whether this mod handles the plant at all (the rest is vanilla behavior).</summary>
        public static bool Handles(Plant plant) =>
            plant != null && plant.Spawned && plant.def.plant != null
            && (!Settings.onlyTrees || plant.def.plant.IsTree);

        public static bool IsPlantCuttingJob(Job job, out Plant plant)
        {
            plant = job.targetA.Thing as Plant;
            if (plant == null)
            {
                return false;
            }
            return job.def == JobDefOf.CutPlant
                || (job.def.driverClass != null && typeof(JobDriver_PlantWork).IsAssignableFrom(job.def.driverClass));
        }

        /// <summary>Whether the builder may still cut the blocking plant itself (vanilla behavior).</summary>
        public static bool BuilderMayCut(Pawn builder, Plant plant)
        {
            if (NeverMarkAutomatically(plant))
            {
                return false;
            }
            if (Settings.skilledBuildersMayCut && PlantsSkill(builder) >= Settings.minPlantsSkill)
            {
                return true;
            }
            return Settings.buildersCutIfNoPlantCutters && !AnyPlantCutterOn(builder.Map);
        }

        public static string FailReason(Plant plant)
        {
            if (HasRemovalOrder(plant))
            {
                return "BDCP.FailReason.WaitingForCutters".Translate(plant.LabelShort);
            }
            if (CuttingPrevented(plant))
            {
                return "BDCP.FailReason.CuttingPrevented".Translate(plant.LabelShort);
            }
            if (NeverMarkAutomatically(plant))
            {
                return "BDCP.FailReason.SpecialPlant".Translate(plant.LabelShort);
            }
            // Only from the right-click menu, which never marks plants itself; the next work scan does.
            return "BDCP.FailReason.NeedsCutting".Translate(plant.LabelShort);
        }

        /// <summary>Marks every plant handled by this mod that blocks the construction site.</summary>
        public static void MarkBlockingPlants(Thing site)
        {
            Map map = site.Map;
            foreach (IntVec3 cell in site.OccupiedRect())
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Plant plant && Handles(plant) && GenConstruct.BlocksConstruction(site, plant))
                    {
                        TryMark(plant);
                    }
                }
            }
        }

        public static void MarkRoofBlocker(IntVec3 cell, Map map)
        {
            if (RoofUtility.FirstBlockingThing(cell, map) is Plant plant && Handles(plant))
            {
                TryMark(plant);
            }
        }

        /// <summary>Gives the plant a vanilla cut order unless something already takes care of removing it.</summary>
        public static void TryMark(Plant plant)
        {
            ResetIfNewGame();
            DesignationManager designations = plant.Map.designationManager;
            if (designations.DesignationOn(plant, DesignationDefOf.CutPlant) != null
                || designations.DesignationOn(plant, DesignationDefOf.ExtractTree) != null)
            {
                return;
            }
            Designation harvest = designations.DesignationOn(plant, DesignationDefOf.HarvestPlant);
            if (harvest != null && plant.HarvestableNow)
            {
                // Plant cutters will harvest (and for trees: chop) it anyway.
                return;
            }
            if (NeverMarkAutomatically(plant))
            {
                WarnOnce(plant);
                return;
            }
            DesignationDef replaced = null;
            if (harvest != null)
            {
                // A harvest order on a plant that isn't ready would never be done, and the site would wait forever.
                designations.RemoveDesignation(harvest);
                replaced = DesignationDefOf.HarvestPlant;
            }
            var cut = new Designation(plant, DesignationDefOf.CutPlant);
            designations.AddDesignation(cut);
            ourMarks.Add(cut, new MarkInfo { replaced = replaced });
        }

        /// <summary>Removes our cut orders from plants under a construction site that is going away, if nothing else needs them gone.</summary>
        public static void UnmarkPlantsUnder(Thing site)
        {
            Map map = site.Map;
            foreach (IntVec3 cell in site.OccupiedRect())
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Plant plant)
                    {
                        UnmarkIfUnneeded(plant, site);
                    }
                }
            }
        }

        public static void UnmarkRoofBlockers(IntVec3 cell, Map map)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Plant plant && plant.def.plant.interferesWithRoof)
                {
                    UnmarkIfUnneeded(plant, null);
                }
            }
        }

        public static void ReportError(string where, Exception e)
        {
            Log.ErrorOnce($"[Builders Don't Cut Plants] Error in {where}, vanilla behavior is used instead: {e}", ("BuildersDontCutPlants." + where).GetHashCode());
        }

        private static void UnmarkIfUnneeded(Plant plant, Thing ignoredSite)
        {
            if (!plant.Spawned)
            {
                return;
            }
            DesignationManager designations = plant.Map.designationManager;
            Designation cut = designations.DesignationOn(plant, DesignationDefOf.CutPlant);
            if (cut == null || !ourMarks.TryGetValue(cut, out MarkInfo info) || StillInTheWay(plant, ignoredSite))
            {
                return;
            }
            ourMarks.Remove(cut);
            designations.RemoveDesignation(cut);
            if (info.replaced != null && designations.DesignationOn(plant, info.replaced) == null)
            {
                designations.AddDesignation(new Designation(plant, info.replaced));
            }
        }

        private static bool StillInTheWay(Plant plant, Thing ignoredSite)
        {
            Map map = plant.Map;
            foreach (IntVec3 cell in plant.OccupiedRect())
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing != ignoredSite && IsConstructionSite(thing) && GenConstruct.BlocksConstruction(thing, plant))
                    {
                        return true;
                    }
                }
                if (Settings.includeRoofs && plant.def.plant.interferesWithRoof && map.areaManager.BuildRoof[cell] && !cell.Roofed(map))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasRemovalOrder(Plant plant)
        {
            DesignationManager designations = plant.Map.designationManager;
            return designations.DesignationOn(plant, DesignationDefOf.CutPlant) != null
                || designations.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null
                || designations.DesignationOn(plant, DesignationDefOf.ExtractTree) != null;
        }

        private static bool NeverMarkAutomatically(Plant plant)
        {
            if (CuttingPrevented(plant))
            {
                return true;
            }
            return Settings.protectSpecialPlants
                && (plant.def.plant.warnIfMarkedForCut || plant.def.plant.treeCategory == TreeCategory.Super);
        }

        private static bool CuttingPrevented(Plant plant)
        {
#if RW_1_6
            // The player toggled "prevent cutting" on this plant; vanilla plant cutters won't touch it either.
            CompPlantPreventCutting comp = plant.GetComp<CompPlantPreventCutting>();
            return comp != null && comp.PreventCutting;
#else
            return false;
#endif
        }

        private static int PlantsSkill(Pawn pawn)
        {
            if (pawn.skills != null)
            {
                return pawn.skills.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
            }
            return pawn.RaceProps.IsMechanoid ? pawn.RaceProps.mechFixedSkillLevel : 0;
        }

        /// <summary>Is any pawn of the player on this map assigned to plant cutting? Cached per tick.</summary>
        private static bool AnyPlantCutterOn(Map map)
        {
            ResetIfNewGame();
            int tick = Find.TickManager.TicksGame;
            if (tick != plantCutterCacheTick)
            {
                plantCutterOnMap.Clear();
                plantCutterCacheTick = tick;
            }
            if (plantCutterOnMap.TryGetValue(map.uniqueID, out bool any))
            {
                return any;
            }
            List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count && !any; i++)
            {
                Pawn pawn = pawns[i];
                any = pawn.workSettings != null
                    && pawn.workSettings.EverWork
                    && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.PlantCutting)
                    && pawn.workSettings.WorkIsActive(WorkTypeDefOf.PlantCutting);
            }
            plantCutterOnMap[map.uniqueID] = any;
            return any;
        }

        private static void WarnOnce(Plant plant)
        {
            if (!warnedPlantIds.Add(plant.thingIDNumber))
            {
                return;
            }
            string key = CuttingPrevented(plant) ? "BDCP.Message.CuttingPrevented" : "BDCP.Message.SpecialPlant";
            Messages.Message(key.Translate(plant.LabelCap), plant, MessageTypeDefOf.CautionInput, historical: false);
        }

        private static void ResetIfNewGame()
        {
            if (cachedGame == Current.Game)
            {
                return;
            }
            cachedGame = Current.Game;
            warnedPlantIds.Clear();
            plantCutterOnMap.Clear();
            plantCutterCacheTick = -1;
        }
    }
}
