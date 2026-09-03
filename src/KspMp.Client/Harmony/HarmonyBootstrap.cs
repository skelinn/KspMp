using System.Linq;
using HarmonyLib;

namespace KspMp.Harmony
{
    internal static class HarmonyBootstrap
    {
        public const string Id = "com.campbellscott.kspmp";

        private static HarmonyLib.Harmony _harmony;

        public static bool Patched { get; private set; }
        public static int PatchCount { get; private set; }

        /// <summary>Applies every [HarmonyPatch] class in this assembly. Idempotent.</summary>
        public static void Patch()
        {
            if (_harmony != null) return;
            _harmony = new HarmonyLib.Harmony(Id);
            _harmony.PatchAll(typeof(HarmonyBootstrap).Assembly);
            PatchCount = _harmony.GetPatchedMethods().Count();
            Patched = true;
            Log.Info("Harmony " + typeof(HarmonyLib.Harmony).Assembly.GetName().Version + " applied " + PatchCount + " patch(es)");
        }
    }
}
