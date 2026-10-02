using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BuildersDontCutPlants
{
    /// <summary>
    /// Only postfixes (plus one cleanup prefix that never skips the original), each applied on its own so a
    /// missing method in some game version or a failing patch only disables that one feature.
    /// Plants are only marked from non-forced job checks: forced checks come from the right-click menu,
    /// and changing the game from there would desync Multiplayer.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class HarmonyPatches
    {
        private const string HarmonyId = "silvanalig95.BuildersDontCutPlants";

        static HarmonyPatches()
        {
            var harmony = new Harmony(HarmonyId);

            Patch(harmony, AccessTools.DeclaredMethod(typeof(GenConstruct), nameof(GenConstruct.HandleBlockingThingJob)), postfix: nameof(GenConstruct_HandleBlockingThingJob));
            foreach (MethodInfo hasJobOnThing in ConstructionHasJobOnThingOverrides())
            {
                Patch(harmony, hasJobOnThing, postfix: nameof(Construction_HasJobOnThing));
            }
            Patch(harmony, AccessTools.DeclaredMethod(typeof(RoofUtility), nameof(RoofUtility.CanHandleBlockingThing)), postfix: nameof(RoofUtility_CanHandleBlockingThing));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(RoofUtility), nameof(RoofUtility.HandleBlockingThingJob)), postfix: nameof(RoofUtility_HandleBlockingThingJob));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(Blueprint), nameof(Blueprint.SpawnSetup)), postfix: nameof(Blueprint_SpawnSetup));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(Blueprint), nameof(Blueprint.DeSpawn)), prefix: nameof(Blueprint_DeSpawn));
            Patch(harmony, AccessTools.DeclaredMethod(typeof(Area), "Set", new[] { typeof(IntVec3), typeof(bool) }), postfix: nameof(Area_Set));
        }

        private static BuildersDontCutPlantsSettings Settings => BuildersDontCutPlantsMod.Settings;

        /// <summary>
        /// Construction work givers that override HasJobOnThing (RimWorld 1.6 does for resource delivery).
        /// It has to say "no job" exactly when JobOnThing returns none, or the work scan logs an error and the
        /// builder idles next to the blocked site.
        /// </summary>
        private static IEnumerable<MethodInfo> ConstructionHasJobOnThingOverrides()
        {
            var types = new HashSet<Type> { typeof(WorkGiver_ConstructDeliverResources), typeof(WorkGiver_ConstructFinishFrames) };
            types.UnionWith(typeof(WorkGiver_ConstructDeliverResources).AllSubclasses());
            types.UnionWith(typeof(WorkGiver_ConstructFinishFrames).AllSubclasses());
            foreach (Type type in types)
            {
                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(WorkGiver_Scanner.HasJobOnThing), new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                if (method != null && !method.IsAbstract)
                {
                    yield return method;
                }
            }
        }

        private static void Patch(Harmony harmony, MethodBase original, string prefix = null, string postfix = null)
        {
            string patchName = prefix ?? postfix;
            if (original == null)
            {
                Log.Warning($"[Builders Don't Cut Plants] Target of patch {patchName} not found in this game version; that part of the mod is disabled.");
                return;
            }
            try
            {
                harmony.Patch(original, prefix: HarmonyMethodFor(prefix), postfix: HarmonyMethodFor(postfix));
            }
            catch (Exception e)
            {
                Log.Error($"[Builders Don't Cut Plants] Could not patch {original.DeclaringType}.{original.Name}; that part of the mod is disabled: {e}");
            }
        }

        private static HarmonyMethod HarmonyMethodFor(string name)
        {
            if (name == null)
            {
                return null;
            }
            // Run after other mods' patches so we see the job that is really about to be handed out.
            return new HarmonyMethod(AccessTools.DeclaredMethod(typeof(HarmonyPatches), name)) { priority = Priority.Last };
        }

        // GenConstruct.HandleBlockingThingJob(Thing constructible, Pawn worker, bool forced): hands a builder the job that
        // clears a blueprint or frame. For a blocking plant that's a cut job, which we replace with a cut order.
        public static void GenConstruct_HandleBlockingThingJob(Thing constructible, Pawn worker, bool forced, ref Job __result)
        {
            try
            {
                if (!PlantCutRedirect.IsPlayerPawn(worker) || !PlantCutRedirect.IsConstructionSite(constructible))
                {
                    return;
                }
                Plant plant;
                if (__result != null)
                {
                    if (!PlantCutRedirect.IsPlantCuttingJob(__result, out plant))
                    {
                        return; // hauling an item aside, deconstructing, mining, ...
                    }
                }
                else
                {
                    // The builder can't or won't cut it (unreachable, ideology, ...): still make sure plant cutters do.
                    plant = GenConstruct.FirstBlockingThing(constructible, worker) as Plant;
                }
                if (!PlantCutRedirect.Handles(plant))
                {
                    return;
                }
                if (!forced)
                {
                    PlantCutRedirect.MarkBlockingPlants(constructible);
                }
                if (__result != null && !PlantCutRedirect.BuilderMayCut(worker, plant))
                {
                    JobFailReason.Is(PlantCutRedirect.FailReason(plant));
                    __result = null;
                }
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(GenConstruct_HandleBlockingThingJob), e);
            }
        }

        // WorkGiver_*.HasJobOnThing(Pawn pawn, Thing t, bool forced) overrides of construction work givers.
        // Arguments by index: modded overrides may name their parameters differently.
        public static void Construction_HasJobOnThing(Pawn __0, Thing __1, bool __2, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            try
            {
                Pawn pawn = __0;
                Thing site = __1;
                bool forced = __2;
                if (!PlantCutRedirect.IsPlayerPawn(pawn) || !PlantCutRedirect.IsConstructionSite(site))
                {
                    return;
                }
                if (!(GenConstruct.FirstBlockingThing(site, pawn) is Plant plant) || !PlantCutRedirect.Handles(plant)
                    || PlantCutRedirect.BuilderMayCut(pawn, plant))
                {
                    return;
                }
                if (!forced)
                {
                    PlantCutRedirect.MarkBlockingPlants(site);
                }
                JobFailReason.Is(PlantCutRedirect.FailReason(plant));
                __result = false;
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(Construction_HasJobOnThing), e);
            }
        }

        // RoofUtility.CanHandleBlockingThing(Thing blocker, Pawn worker, bool forced): WorkGiver_BuildRoof.HasJobOnCell.
        public static void RoofUtility_CanHandleBlockingThing(Thing blocker, Pawn worker, bool forced, ref bool __result)
        {
            try
            {
                if (!Settings.includeRoofs || !(blocker is Plant plant) || !PlantCutRedirect.Handles(plant) || !PlantCutRedirect.IsPlayerPawn(worker))
                {
                    return;
                }
                if (!forced)
                {
                    PlantCutRedirect.TryMark(plant);
                }
                if (__result && !PlantCutRedirect.BuilderMayCut(worker, plant))
                {
                    JobFailReason.Is(PlantCutRedirect.FailReason(plant));
                    __result = false;
                }
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(RoofUtility_CanHandleBlockingThing), e);
            }
        }

        // RoofUtility.HandleBlockingThingJob(Thing blocker, Pawn worker, bool forced): WorkGiver_BuildRoof.JobOnCell.
        public static void RoofUtility_HandleBlockingThingJob(Thing blocker, Pawn worker, bool forced, ref Job __result)
        {
            if (__result == null)
            {
                return;
            }
            try
            {
                if (!Settings.includeRoofs || !(blocker is Plant plant) || !PlantCutRedirect.Handles(plant) || !PlantCutRedirect.IsPlayerPawn(worker)
                    || !PlantCutRedirect.IsPlantCuttingJob(__result, out _))
                {
                    return;
                }
                if (!forced)
                {
                    PlantCutRedirect.TryMark(plant);
                }
                if (!PlantCutRedirect.BuilderMayCut(worker, plant))
                {
                    JobFailReason.Is(PlantCutRedirect.FailReason(plant));
                    __result = null;
                }
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(RoofUtility_HandleBlockingThingJob), e);
            }
        }

        // Blueprint.SpawnSetup(Map map, bool respawningAfterLoad): a blueprint was placed.
        public static void Blueprint_SpawnSetup(Blueprint __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad)
            {
                return;
            }
            try
            {
                if (Settings.markOnPlacement && PlantCutRedirect.IsConstructionSite(__instance))
                {
                    PlantCutRedirect.MarkBlockingPlants(__instance);
                }
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(Blueprint_SpawnSetup), e);
            }
        }

        // Blueprint.DeSpawn(DestroyMode mode): the blueprint was cancelled, replaced or turned into a frame.
        public static void Blueprint_DeSpawn(Blueprint __instance)
        {
            try
            {
                if (__instance.Spawned)
                {
                    PlantCutRedirect.UnmarkPlantsUnder(__instance);
                }
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(Blueprint_DeSpawn), e);
            }
        }

        // Area.Set(IntVec3 c, bool val): a cell was added to or removed from an area, here the build-roof area.
        public static void Area_Set(Area __instance, IntVec3 c, bool val)
        {
            if (!(__instance is Area_BuildRoof))
            {
                return;
            }
            try
            {
                Map map = __instance.Map;
                if (map == null || !c.InBounds(map))
                {
                    return;
                }
                if (!val)
                {
                    PlantCutRedirect.UnmarkRoofBlockers(c, map);
                }
                else if (Settings.includeRoofs && Settings.markOnPlacement && !c.Roofed(map))
                {
                    PlantCutRedirect.MarkRoofBlocker(c, map);
                }
            }
            catch (Exception e)
            {
                PlantCutRedirect.ReportError(nameof(Area_Set), e);
            }
        }
    }
}
