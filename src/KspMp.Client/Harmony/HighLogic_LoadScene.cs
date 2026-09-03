using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>M0 smoke-test patch: proves Harmony works in this build by logging every scene change request.</summary>
    [HarmonyPatch(typeof(HighLogic), nameof(HighLogic.LoadScene), typeof(GameScenes))]
    internal static class HighLogic_LoadScene
    {
        private static void Postfix(GameScenes scene)
        {
            Log.Info("Scene load requested: " + scene);
        }
    }
}
