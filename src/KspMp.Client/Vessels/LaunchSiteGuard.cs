using System;
using System.Collections.Generic;
using UnityEngine;

namespace KspMp.Vessels
{
    /// <summary>
    /// Stops two players launching onto the same pad. KSP's own pre-flight check only counts vessels it
    /// considers landed at the site, which misses a friend's rocket that arrived as a replicated proto, so
    /// both craft spawn inside each other and are destroyed with everyone aboard.
    ///
    /// Two things are checked. A vessel actually sitting on the site is the ordinary case: someone launched
    /// a minute ago and has not flown off yet. A launch announced by another player very recently covers the
    /// race the vessel check cannot see, where both players press Launch before either craft exists.
    /// </summary>
    public static class LaunchSiteGuard
    {
        /// <summary>How long another player's announced launch keeps a site reserved.</summary>
        public const float RecentLaunchSeconds = 40f;

        private static readonly Dictionary<string, Entry> Recent = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private struct Entry
        {
            public string PlayerName;
            public float At;
        }

        /// <summary>Another player just launched here; hold the site briefly even before their vessel exists.</summary>
        public static void NoteRemoteLaunch(string site, string playerName)
        {
            if (string.IsNullOrEmpty(site)) return;
            Recent[site] = new Entry { PlayerName = playerName ?? "another player", At = Time.realtimeSinceStartup };
        }

        /// <summary>Our own launch went through, so stop holding the site against ourselves.</summary>
        public static void Clear(string site)
        {
            if (!string.IsNullOrEmpty(site)) Recent.Remove(site);
        }

        public static void Reset() => Recent.Clear();

        /// <summary>True when launching at this site would drop a craft on top of something.</summary>
        public static bool IsBlocked(string site, VesselRegistry registry, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(site)) return false;

            if (Recent.TryGetValue(site, out var entry))
            {
                var age = Time.realtimeSinceStartup - entry.At;
                if (age < RecentLaunchSeconds)
                {
                    reason = entry.PlayerName + " is launching from the " + site + " right now. Give them "
                             + Mathf.CeilToInt(RecentLaunchSeconds - age) + "s to clear it.";
                    return true;
                }
                Recent.Remove(site);
            }

            var occupant = FindOccupant(site, registry, out var owned);
            if (occupant == null) return false;
            reason = (owned ?? "A vessel") + " is still sitting on the " + site
                     + " (" + occupant + "). Launching now would destroy both craft.";
            return true;
        }

        /// <summary>The display name of whatever is parked on the site, or null when it is clear.</summary>
        private static string FindOccupant(string site, VesselRegistry registry, out string owner)
        {
            owner = null;
            var vessels = FlightGlobals.Vessels;
            if (vessels == null) return null;
            for (var i = 0; i < vessels.Count; i++)
            {
                var v = vessels[i];
                if (v == null || PartCount(v) == 0) continue;
                if (v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Unknown
                    || v.vesselType == VesselType.Debris && v.situation != Vessel.Situations.PRELAUNCH) continue;
                if (!SitsOn(v, site)) continue;
                if (registry != null && registry.IsOwnedByOther(v.id)) owner = "Another player's craft";
                return v.GetDisplayName();
            }
            return null;
        }

        /// <summary>
        /// Parts, whether or not the vessel is loaded. An unloaded vessel has an empty parts list and lives
        /// only as a proto, and from the space center every other player's craft is unloaded - so counting
        /// parts alone would quietly skip exactly the vessel that is sitting on the pad.
        /// </summary>
        private static int PartCount(Vessel vessel)
        {
            if (vessel.loaded) return vessel.parts != null ? vessel.parts.Count : 0;
            return vessel.protoVessel != null && vessel.protoVessel.protoPartSnapshots != null
                ? vessel.protoVessel.protoPartSnapshots.Count
                : 0;
        }

        /// <summary>
        /// A vessel occupies a launch site when it has not moved off it. PRELAUNCH means exactly that, and a
        /// landed vessel whose landedAt names the site covers a craft that was launched and then left there.
        /// </summary>
        private static bool SitsOn(Vessel vessel, string site)
        {
            if (vessel.situation == Vessel.Situations.PRELAUNCH) return MatchesSite(vessel, site) || NoSiteRecorded(vessel);
            if (vessel.situation != Vessel.Situations.LANDED) return false;
            return MatchesSite(vessel, site);
        }

        private static bool MatchesSite(Vessel vessel, string site)
        {
            return Names(vessel).Exists(n => n.IndexOf(site, StringComparison.OrdinalIgnoreCase) >= 0
                                             || site.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool NoSiteRecorded(Vessel vessel) => Names(vessel).Count == 0;

        private static List<string> Names(Vessel vessel)
        {
            var names = new List<string>(2);
            if (!string.IsNullOrEmpty(vessel.landedAt)) names.Add(vessel.landedAt);
            if (!string.IsNullOrEmpty(vessel.displaylandedAt)) names.Add(vessel.displaylandedAt);
            return names;
        }
    }
}
