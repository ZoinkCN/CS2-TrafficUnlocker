using Colossal;
using System.Collections.Generic;

namespace TrafficUnlocker.Locales
{
    public class LocaleCNT : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleCNT(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "交通解鎖" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TrafficReduction)), "交通抑製" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TrafficReduction)), "設置交通抑製因素。範圍從 0 到 0.1。\r\n實際生效值為：設置值/10000 。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.WorkProbability)), "上班概率" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.WorkProbability)), "設置市民上班的概率。範圍從 0 到 100。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SchoolProbability)), "上學概率" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SchoolProbability)), "設置學生上學的概率。範圍從 0 到 100。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LeisureProbability)), "閒置概率" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LeisureProbability)), "設置市民閒置的概率。範圍從 0 到 100。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Reset)), "重設" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Reset)), "將所有參數重設爲默認值" },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.Reset)), "是否重設所有參數?" },
            };
        }

        public void Unload()
        {

        }
    }
}
