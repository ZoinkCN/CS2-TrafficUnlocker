using Game;
using Game.Simulation;
using HarmonyLib;
using TrafficUnlocker.Systems;

namespace TrafficUnlocker.Patches
{
    [HarmonyPatch(typeof(CitizenBehaviorSystem))]
    public class CitizenBehaviorSystemPatches
    {
        [HarmonyPatch("OnCreate")]
        [HarmonyPrefix]
        private static bool OnCreatePrefix(CitizenBehaviorSystem __instance)
        {
            __instance.World.GetOrCreateSystemManaged<MyCitizenBehaviorSystem>();
            __instance.World.GetOrCreateSystemManaged<UpdateSystem>().UpdateAt<MyCitizenBehaviorSystem>(SystemUpdatePhase.GameSimulation);
            return true;
        }

        [HarmonyPatch("OnCreateForCompiler")]
        [HarmonyPrefix]
        private static bool OnCreateForCompilerPrefix(CitizenBehaviorSystem __instance)
        {
            return false;
        }

        [HarmonyPatch("OnUpdate")]
        [HarmonyPrefix]
        private static bool OnUpdatePrefix(CitizenBehaviorSystem __instance)
        {
            __instance.World.GetOrCreateSystemManaged<MyCitizenBehaviorSystem>().Update();
            return false;
        }
    }
}
