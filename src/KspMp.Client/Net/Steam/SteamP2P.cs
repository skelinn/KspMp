using System;

namespace KspMp.Net.Steam
{
    /// <summary>
    /// Brings up Steam inside KSP and hands out the two interfaces the transport needs.
    ///
    /// Everything here is optional: a player without Steam, or running a copy of KSP that Steam does not know
    /// about, simply gets <see cref="Available"/> false and carries on with the normal UDP transport. Nothing
    /// in the mod should require Steam to be there.
    /// </summary>
    public static class SteamP2P
    {
        private static bool _tried;
        private static IntPtr _networking;
        private static IntPtr _user;

        /// <summary>True once Steam is up and the P2P interface answered.</summary>
        public static bool Available { get; private set; }
        /// <summary>Our own Steam ID, which is the address other players connect to. 0 when unavailable.</summary>
        public static ulong LocalSteamId { get; private set; }
        /// <summary>Why Steam is not available, for the log and the connect window.</summary>
        public static string Unavailable { get; private set; } = "not initialised yet";

        internal static IntPtr Networking => _networking;

        /// <summary>
        /// Safe to call repeatedly; only the first attempt does anything. Failure is expected and quiet: the
        /// entry points are missing on a KSP that ships no Steam native at all, and SteamAPI_Init returns
        /// false when the game was not launched through Steam and has no steam_appid.txt beside it.
        /// </summary>
        public static bool TryInitialise()
        {
            if (_tried) return Available;
            _tried = true;
            try
            {
                if (!SteamNative.SteamAPI_Init())
                {
                    Unavailable = "Steam is not running, or this copy of KSP was not launched through Steam "
                                  + "(a steam_appid.txt containing 220200 next to KSP_x64.exe also works).";
                    return false;
                }

                var client = SteamNative.SteamInternal_CreateInterface("SteamClient017");
                if (client == IntPtr.Zero) { Unavailable = "Steam did not hand out SteamClient017."; return false; }

                var hUser = SteamNative.SteamAPI_GetHSteamUser();
                var hPipe = SteamNative.SteamAPI_GetHSteamPipe();
                if (hPipe == 0) { Unavailable = "Steam gave us no pipe; is anyone signed in?"; return false; }

                _user = SteamNative.SteamAPI_ISteamClient_GetISteamUser(client, hUser, hPipe, "SteamUser019");
                if (_user != IntPtr.Zero) LocalSteamId = SteamNative.SteamAPI_ISteamUser_GetSteamID(_user);

                // The version is supplied by us, not the library, so try the ones this API has shipped as.
                foreach (var version in new[] { "SteamNetworking006", "SteamNetworking005", "SteamNetworking004" })
                {
                    _networking = SteamNative.SteamAPI_ISteamClient_GetISteamNetworking(client, hUser, hPipe, version);
                    if (_networking != IntPtr.Zero)
                    {
                        Log.Info("Steam: using " + version);
                        break;
                    }
                }
                if (_networking == IntPtr.Zero)
                {
                    Unavailable = "Steam has no ISteamNetworking interface this build understands.";
                    return false;
                }

                // Let Steam fall back to its own relay when a direct link cannot be made. This is the whole
                // point of going through Steam: it is what reaches players behind carrier-grade NAT.
                SteamNative.SteamAPI_ISteamNetworking_AllowP2PPacketRelay(_networking, true);

                Available = true;
                Unavailable = null;
                Log.Info("Steam ready; your Steam ID is " + LocalSteamId + ". Friends can join with that.");
                return true;
            }
            catch (DllNotFoundException)
            {
                Unavailable = "This KSP install ships no steam_api64.dll.";
            }
            catch (EntryPointNotFoundException e)
            {
                Unavailable = "This KSP's Steam library is missing " + e.Message + ".";
            }
            catch (Exception e)
            {
                Unavailable = e.GetType().Name + ": " + e.Message;
            }
            return false;
        }

        /// <summary>Steam wants its callbacks pumped; harmless when Steam never came up.</summary>
        public static void Poll()
        {
            if (!Available) return;
            try { SteamNative.SteamAPI_RunCallbacks(); }
            catch (Exception e) { Log.Exception("Steam callbacks", e); }
        }

        /// <summary>Describes how a session with this player is actually routed, once one exists.</summary>
        public static string DescribeRoute(ulong steamId)
        {
            if (!Available) return "Steam unavailable";
            if (!SteamNative.SteamAPI_ISteamNetworking_GetP2PSessionState(_networking, steamId, out var state))
                return "no session";
            if (state.P2PSessionError != 0) return "error " + state.P2PSessionError;
            if (state.Connecting != 0) return "connecting";
            if (state.ConnectionActive == 0) return "inactive";
            return state.UsingRelay != 0 ? "connected via Steam relay" : "connected directly";
        }
    }
}
