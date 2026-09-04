using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>Nobody but the owner may fire or kill another player's Kerbal.</summary>
    [HarmonyPatch(typeof(KerbalRoster), nameof(KerbalRoster.SackAvailable), typeof(ProtoCrewMember))]
    internal static class KerbalRoster_SackAvailable
    {
        private static bool Prefix(ProtoCrewMember ap)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Roster == null || ap == null || !addon.Roster.IsOtherPlayersAvatar(ap.name)) return true;
            ScreenMessages.PostScreenMessage(ap.name + " is " + addon.Roster.AvatarOwnerName(ap.name) + "'s Kerbal", 3f, ScreenMessageStyle.UPPER_CENTER);
            return false;
        }
    }

    [HarmonyPatch(typeof(ProtoCrewMember), nameof(ProtoCrewMember.Die))]
    internal static class ProtoCrewMember_Die
    {
        private static bool Prefix(ProtoCrewMember __instance)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Roster == null || !addon.Roster.IsOtherPlayersAvatar(__instance.name)) return true;
            Log.Warn("Ignoring a local death of " + __instance.name + ", another player's Kerbal (their client reports it)");
            return false;
        }
    }
}
