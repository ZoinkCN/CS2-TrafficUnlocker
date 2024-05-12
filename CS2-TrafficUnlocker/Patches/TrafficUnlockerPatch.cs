//using Game;
//using Game.Common;
//using HarmonyLib;
//using TrafficUnlocker.Systems;

//namespace TrafficUnlocker.Patches
//{
//    [HarmonyPatch(typeof(SystemOrder))]
//    public static class SystemOrderPatch
//    {
//        [HarmonyPatch("Initialize")]
//        [HarmonyPostfix]
//        public static void Postfix(UpdateSystem updateSystem)
//        {
//            updateSystem.UpdateBefore<TrafficReductionSystem>(SystemUpdatePhase.GameSimulation);
//        }
//    }
//}
