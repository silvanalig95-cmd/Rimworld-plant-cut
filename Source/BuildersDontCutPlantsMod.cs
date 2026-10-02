using UnityEngine;
using Verse;

namespace BuildersDontCutPlants
{
    public class BuildersDontCutPlantsMod : Mod
    {
        public static BuildersDontCutPlantsSettings Settings { get; private set; }

        public BuildersDontCutPlantsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BuildersDontCutPlantsSettings>();
        }

        public override string SettingsCategory() => Content.Name;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            BuildersDontCutPlantsSettings s = Settings;
            var list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("BDCP.Settings.Intro".Translate());
            list.GapLine();

            list.CheckboxLabeled("BDCP.Settings.MarkOnPlacement".Translate(), ref s.markOnPlacement, "BDCP.Settings.MarkOnPlacement.Tip".Translate());
            list.CheckboxLabeled("BDCP.Settings.IncludeRoofs".Translate(), ref s.includeRoofs, "BDCP.Settings.IncludeRoofs.Tip".Translate());
            list.CheckboxLabeled("BDCP.Settings.OnlyTrees".Translate(), ref s.onlyTrees, "BDCP.Settings.OnlyTrees.Tip".Translate());
            list.CheckboxLabeled("BDCP.Settings.ProtectSpecial".Translate(), ref s.protectSpecialPlants, "BDCP.Settings.ProtectSpecial.Tip".Translate());
            list.GapLine();

            list.CheckboxLabeled("BDCP.Settings.NoCuttersFallback".Translate(), ref s.buildersCutIfNoPlantCutters, "BDCP.Settings.NoCuttersFallback.Tip".Translate());
            list.CheckboxLabeled("BDCP.Settings.SkilledBuilders".Translate(), ref s.skilledBuildersMayCut, "BDCP.Settings.SkilledBuilders.Tip".Translate());
            if (s.skilledBuildersMayCut)
            {
                list.Label("BDCP.Settings.MinSkill".Translate(s.minPlantsSkill));
                s.minPlantsSkill = Mathf.RoundToInt(list.Slider(s.minPlantsSkill, 0f, 20f));
            }

            list.Gap();
            if (list.ButtonText("BDCP.Settings.Reset".Translate()))
            {
                s.ResetToDefaults();
            }

            list.End();
        }
    }
}
