using Game;
using Game.Simulation;
using HarmonyLib;
using TrafficUnlocker.Systems;

namespace TrafficUnlocker.Patches
{
    public class WorkerSystemPatches
    {
        [HarmonyPatch(typeof(WorkerSystem))]
        public class StudentSystemPathches
        {
            [HarmonyPatch("OnCreate")]
            [HarmonyPrefix]
            private static bool OnCreatePrefix(WorkerSystem __instance)
            {
                __instance.World.GetOrCreateSystemManaged<MyWorkerSystem>();
                __instance.World.GetOrCreateSystemManaged<UpdateSystem>().UpdateAt<MyWorkerSystem>(SystemUpdatePhase.GameSimulation);
                return true;
            }

            [HarmonyPatch("OnCreateForCompiler")]
            [HarmonyPrefix]
            private static bool OnCreateForCompilerPrefix(WorkerSystem __instance)
            {
                return false;
            }

            [HarmonyPatch("OnUpdate")]
            [HarmonyPrefix]
            private static bool OnUpdatePrefix(WorkerSystem __instance)
            {
                __instance.World.GetOrCreateSystemManaged<MyWorkerSystem>().Update();
                return false;
            }
        }
    }
}
