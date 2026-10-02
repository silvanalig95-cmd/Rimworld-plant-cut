using Verse;

namespace BuildersDontCutPlants
{
    public class BuildersDontCutPlantsSettings : ModSettings
    {
        /// <summary>Mark blocking plants the moment a blueprint or build-roof area is placed, not only when a builder gets to it.</summary>
        public bool markOnPlacement = true;

        /// <summary>Also redirect plants that block roof building (trees in a build-roof area).</summary>
        public bool includeRoofs = true;

        /// <summary>Only redirect trees; builders keep clearing other plants (bushes, crops, ...) themselves.</summary>
        public bool onlyTrees;

        /// <summary>Never mark special plants (anima tree, Gauranlen tree, ...) automatically; the player has to do it.</summary>
        public bool protectSpecialPlants = true;

        /// <summary>Let builders cut themselves when nobody on the map is assigned to plant cutting, so construction can't get stuck.</summary>
        public bool buildersCutIfNoPlantCutters = true;

        /// <summary>Let builders with at least <see cref="minPlantsSkill"/> in Plants cut themselves.</summary>
        public bool skilledBuildersMayCut;

        public int minPlantsSkill = DefaultMinPlantsSkill;

        private const int DefaultMinPlantsSkill = 10;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref markOnPlacement, "markOnPlacement", true);
            Scribe_Values.Look(ref includeRoofs, "includeRoofs", true);
            Scribe_Values.Look(ref onlyTrees, "onlyTrees", false);
            Scribe_Values.Look(ref protectSpecialPlants, "protectSpecialPlants", true);
            Scribe_Values.Look(ref buildersCutIfNoPlantCutters, "buildersCutIfNoPlantCutters", true);
            Scribe_Values.Look(ref skilledBuildersMayCut, "skilledBuildersMayCut", false);
            Scribe_Values.Look(ref minPlantsSkill, "minPlantsSkill", DefaultMinPlantsSkill);
        }

        public void ResetToDefaults()
        {
            markOnPlacement = true;
            includeRoofs = true;
            onlyTrees = false;
            protectSpecialPlants = true;
            buildersCutIfNoPlantCutters = true;
            skilledBuildersMayCut = false;
            minPlantsSkill = DefaultMinPlantsSkill;
        }
    }
}
