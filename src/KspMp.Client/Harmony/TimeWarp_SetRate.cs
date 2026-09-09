using HarmonyLib;
using KspMp.Shared.Protocol;
using KspMp.Systems;

namespace KspMp.Harmony
{
    /// <summary>
    /// Warp changes become requests to the server; only the server's answer actually changes the rate.
    ///
    /// The private instance setRate, not the public static SetRate: the warp keys, the altitude clamps and
    /// KSP's own resets all call the instance method directly (TimeWarp.cs: the key handlers, the "cannot
    /// warp here" drops), and only the UI buttons and mods go through the static one. Patching the static
    /// one let the "." key warp a player's game locally with the server none the wiser, so their clock ran
    /// four times faster than everyone else's and the shared clock snapped their rocket back every few
    /// seconds - "time warp is very buggy".
    ///
    /// The call is let through: the instance method is also how KSP keeps its own rate bookkeeping
    /// (the reset on load, the altitude clamps, the drop when warp becomes impossible), and blocking it
    /// left the game's clock at 0x. Instead the change is reported as a request and, if the server's
    /// answer differs, the server's rate is put back half a second later (WarpSystem.Update).
    /// </summary>
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
            // Right after a scene load KSP resets warp to 1x and may restore the rate the save was made at.
            // The reset passes; a rate above 1x does not - not KSP's restored one, and not a key pressed in
            // those seconds either. The server's rate is put back a second after the load.
            if (addon.Warp.InSceneGrace) return true;
            var mode = TimeWarp.fetch != null && TimeWarp.fetch.Mode == TimeWarp.Modes.LOW ? WarpMode.Physics : WarpMode.Rails;
            addon.Warp.RequestFromUser(mode, rateIdx);
            return true;
        }
    }
}
