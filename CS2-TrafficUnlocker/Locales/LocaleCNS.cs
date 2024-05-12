using Colossal;
using TrafficUnlocker;
using Game.Settings;
using Game.UI.Widgets;
using System.Collections.Generic;
using Setting = TrafficUnlocker.Setting;

namespace TrafficUnlocker.Locales
{
    public class LocaleCNS : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleCNS(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "交通解锁" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TrafficReduction)), "交通抑制" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TrafficReduction)), "设置交通抑制因素。范围从 0 到 0.1。\r\n实际生效值为：设置值/10000 。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.WorkProbability)), "上班概率" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.WorkProbability)), "设置市民上班的概率。范围从 0 到 100。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SchoolProbability)), "上学概率" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SchoolProbability)), "设置学生上学的概率。范围从 0 到 100。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LeisureProbability)), "闲置概率" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LeisureProbability)), "设置市民闲置的概率。范围从 0 到 100。" },
            };
        }

        public void Unload()
        {

        }
    }
}
