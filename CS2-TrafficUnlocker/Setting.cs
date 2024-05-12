using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace TrafficUnlocker
{
    [FileLocation(nameof(TrafficUnlocker))]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISlider(min = 0, max = 1000, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int TrafficReduction { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int WorkProbability { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int SchoolProbability { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        public int LeisureProbability { get; set; }

        public float GetRealTrafficReduction()
        {
            return TrafficReduction / 10000f;
        }

        public override void SetDefaults()
        {
            TrafficReduction = 4;
            WorkProbability = 40;
            SchoolProbability = 40;
            LeisureProbability = 20;
        }
    }
}
