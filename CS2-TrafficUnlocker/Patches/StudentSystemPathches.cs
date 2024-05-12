using Game;
using Game.Simulation;
using HarmonyLib;
using TrafficUnlocker.Systems;

namespace TrafficUnlocker.Patches
{
    [HarmonyPatch(typeof(StudentSystem))]
    public class StudentSystemPathches
    {
        [HarmonyPatch("OnCreate")]
        [HarmonyPrefix]
        private static bool OnCreatePrefix(StudentSystem __instance)
        {
            __instance.World.GetOrCreateSystemManaged<MyStudentSystem>();
            __instance.World.GetOrCreateSystemManaged<UpdateSystem>().UpdateAt<MyStudentSystem>(SystemUpdatePhase.GameSimulation);
            return true;
        }

        [HarmonyPatch("OnCreateForCompiler")]
        [HarmonyPrefix]
        private static bool OnCreateForCompilerPrefix(StudentSystem __instance)
        {
            return false;
        }

        [HarmonyPatch("OnUpdate")]
        [HarmonyPrefix]
        private static bool OnUpdatePrefix(StudentSystem __instance)
        {
            __instance.World.GetOrCreateSystemManaged<MyStudentSystem>().Update();
            return false;
        }
    }
}
