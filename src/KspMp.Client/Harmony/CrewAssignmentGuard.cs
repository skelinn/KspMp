using System.Collections.Generic;
using HarmonyLib;

namespace KspMp.Harmony
{
    /// <summary>
    /// KSP fills empty seats with whoever is available, which in multiplayer can quietly conscript another player's
    /// Kerbal into your rocket. Flying together should be a decision, so the automatic fill never picks someone
    /// else's avatar. Seating a friend on purpose still works: the crew dialog assigns them directly.
    /// </summary>
    [HarmonyPatch(typeof(KerbalRoster), nameof(KerbalRoster.DefaultCrewForVessel))]
    internal static class KerbalRoster_DefaultCrewForVessel
    {
        private static void Postfix(VesselCrewManifest __result)
        {
            var addon = KspMpAddon.Instance;
            if (addon == null || addon.Roster == null || __result == null) return;
            if (addon.Network == null || !addon.Network.IsConnected) return;

            var conscripted = new List<string>();
            foreach (var crew in __result.GetAllCrew(false))
                if (crew != null && addon.Roster.IsOtherPlayersAvatar(crew.name)) conscripted.Add(crew.name);

            foreach (var name in conscripted)
            {
                __result.RemoveCrewMember(name);
                Log.Info("Left " + name + " behind: they are " + addon.Roster.AvatarOwnerName(name) + "'s Kerbal, not automatic crew");
            }
        }
    }
}
