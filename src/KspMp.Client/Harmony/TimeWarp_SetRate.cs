using HarmonyLib;
using KspMp.Shared.Protocol;
using KspMp.Systems;
using UnityEngine;

namespace KspMp.Harmony
{
    /// <summary>
    /// Warp changes the player asks for become requests to the server; only the server's answer is the shared
    /// rate. KSP's own rate bookkeeping is left alone.
    ///
    /// Both entry points matter and they mean different things. The public static <c>SetRate</c> is what the
    /// warp buttons, the scrubber and other mods call. The private instance <c>setRate</c> is what the warp
    /// keys reach - and also what KSP calls for itself, constantly: clamping to the altitude limit, stepping
    /// the rate down when it cannot sustain one, dropping to 1x over the surface. Patching the private one and
    /// treating every call as a wish turned all of that into network traffic: a real session sent nearly two
    /// thousand warp requests in eighteen minutes and cancelled everyone's warp four hundred times, because
    /// KSP's own "I cannot warp here" drop to 1x read as the player dropping warp for everybody.
    ///
    /// So a call counts as the player's only when it came through the public method or in the same frame as a
    /// warp key going down. Everything else runs locally and is reported as a limit instead, through the cap
    /// in <see cref="WarpSystem"/> - which is what it always was.
    /// </summary>
    [HarmonyPatch(typeof(TimeWarp), nameof(TimeWarp.SetRate), typeof(int), typeof(bool), typeof(bool))]
    internal static class TimeWarp_SetRatePublic
    {
        /// <summary>Set while KSP is inside the public entry point, which only a button, a scrubber or a mod calls.</summary>
        public static bool Inside;

        private static void Prefix(out bool __state)
        {
            __state = Inside;
            Inside = true;
        }

        private static void Postfix(bool __state) => Inside = __state;
    }

    [HarmonyPatch(typeof(TimeWarp), "setRate", typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
    internal static class TimeWarp_SetRate
    {
        private static bool Prefix(int rateIdx, ref bool instantChange)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Network == null || !addon.Network.IsConnected || addon.Warp == null) return true;
            // Coming down is instant: KSP winds rails warp down over several seconds, and a client still at
            // 40x while the server is already at 1x runs twenty seconds ahead and is snapped back.
            if (rateIdx < TimeWarp.CurrentRateIndex) instantChange = true;
            if (WarpSystem.ApplyingServerState) return true;
            // Right after a scene load KSP resets warp and may restore the rate the save was made at. That is
            // not a wish either; the server's rate is put back a second after the load.
            if (addon.Warp.InSceneGrace) return true;
            if (!TimeWarp_SetRatePublic.Inside && !WarpKeyPressed()) return true;   // KSP's own bookkeeping

            var mode = TimeWarp.fetch != null && TimeWarp.fetch.Mode == TimeWarp.Modes.LOW ? WarpMode.Physics : WarpMode.Rails;
            addon.Warp.RequestFromUser(mode, rateIdx);
            return true;
        }

        /// <summary>
        /// True during the frame a warp key went down. KSP handles those keys inside TimeWarp.Update and calls
        /// setRate from there, so the key still reads as pressed when the call arrives.
        /// </summary>
        private static bool WarpKeyPressed()
        {
            try
            {
                return GameSettings.TIME_WARP_INCREASE.GetKeyDown()
                       || GameSettings.TIME_WARP_DECREASE.GetKeyDown()
                       || GameSettings.TIME_WARP_STOP.GetKeyDown();
            }
            catch
            {
                return false;
            }
        }
    }
}
