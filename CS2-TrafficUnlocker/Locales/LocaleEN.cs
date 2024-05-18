using Colossal;
using System.Collections.Generic;

namespace TrafficUnlocker.Locales
{
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "TrafficUnlocker" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TrafficReduction)), "TrafficReduction" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TrafficReduction)), "Set TrafficReduction factor. Range form 0 to 1000.\r\nActual value: SettingValue/10000 ." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.WorkProbability)), "WorkProbability" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.WorkProbability)), "Set the probability that citizens go to work. Range form 0 to 100." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SchoolProbability)), "SchoolProbability" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SchoolProbability)), "Set the probability that students go to school. Range form 0 to 100." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LeisureProbability)), "LeisureProbability" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LeisureProbability)), "Set the probability that citizens do leisure. Range form 0 to 100." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Reset)), "Reset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Reset)), "Reset all the parameters to defualt." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.Reset)), "Do you want reset all the parameters?" },
            };
        }

        public void Unload()
        {

        }
    }
}
