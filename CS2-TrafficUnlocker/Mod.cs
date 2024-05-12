﻿using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using TrafficUnlocker.Locales;
using TrafficUnlocker.Systems;

namespace TrafficUnlocker
{
    public class Mod : IMod
    {
        public static readonly string m_Id = typeof(Mod).Assembly.GetName().Name;
        public static ILog log = LogManager.GetLogger($"{nameof(TrafficUnlocker)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            GameManager.instance.localizationManager.AddSource("zh-HANS", new LocaleCNS(m_Setting));
            GameManager.instance.localizationManager.AddSource("zh-HANT", new LocaleCNT(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(TrafficUnlocker), m_Setting, new Setting(this));

            updateSystem.UpdateBefore<TrafficReductionSystem>(SystemUpdatePhase.GameSimulation);
            var harmony = new Harmony(m_Id);
            harmony.PatchAll();
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
