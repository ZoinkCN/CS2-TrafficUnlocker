using Game;
using Game.Prefabs;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Entities;

namespace TrafficUnlocker.Systems
{
    public partial class TrafficReductionSystem : GameSystemBase
    {
        private EntityQuery m_EconomyParameterGroup;
        private EconomyParameterData m_EconomyParameters;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EconomyParameterGroup = base.GetEntityQuery(new ComponentType[]
            {
                ComponentType.ReadOnly<EconomyParameterData>()
            });

            m_EconomyParameters = m_EconomyParameterGroup.GetSingleton<EconomyParameterData>();
            Mod.log.Info($"TrafficReduction to {m_EconomyParameters.m_TrafficReduction}");
        }

        protected override void OnUpdate()
        {
            try
            {
                float trafficReduction = Mod.m_Setting.GetRealTrafficReduction();
                if (m_EconomyParameters.m_TrafficReduction != trafficReduction)
                {
                    m_EconomyParameters.m_TrafficReduction = trafficReduction;
                    Mod.log.Info($"Changed TrafficReduction to {trafficReduction}");
                }
            }
            catch { }

        }
    }
}
