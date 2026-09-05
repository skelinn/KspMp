using System;
using System.Runtime.InteropServices;

namespace KspMp.Net.Steam
{
    /// <summary>
    /// The handful of Steam entry points this mod needs, bound by hand.
    ///
    /// We deliberately do not ship Steamworks.NET or a native library of our own. KSP already loads
    /// steam_api64.dll from KSP_x64_Data/Plugins/x86_64 for Squad's controller plugin, and Windows loads one
    /// DLL of that name per process, so a second copy could never be reached anyway. Binding by name here
    /// resolves to the module KSP already has open.
    ///
    /// That native is Steamworks 1.42 era (SteamClient017, SteamUser019), which has no SteamNetworkingSockets
    /// and therefore no Steam Datagram Relay. The older ISteamNetworking P2P API below is present in full,
    /// including AllowP2PPacketRelay - Steam's own relay fallback when a direct connection cannot be made,
    /// which is what carries players behind carrier-grade NAT.
    /// </summary>
    internal static class SteamNative
    {
        private const string Lib = "steam_api64";
        private const CallingConvention Cdecl = CallingConvention.Cdecl;

        [DllImport(Lib, CallingConvention = Cdecl)] internal static extern bool SteamAPI_Init();
        [DllImport(Lib, CallingConvention = Cdecl)] internal static extern void SteamAPI_Shutdown();
        [DllImport(Lib, CallingConvention = Cdecl)] internal static extern void SteamAPI_RunCallbacks();
        [DllImport(Lib, CallingConvention = Cdecl)] internal static extern int SteamAPI_GetHSteamUser();
        [DllImport(Lib, CallingConvention = Cdecl)] internal static extern int SteamAPI_GetHSteamPipe();

        [DllImport(Lib, CallingConvention = Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr SteamInternal_CreateInterface(string version);

        [DllImport(Lib, CallingConvention = Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr SteamAPI_ISteamClient_GetISteamUser(IntPtr client, int user, int pipe, string version);

        [DllImport(Lib, CallingConvention = Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr SteamAPI_ISteamClient_GetISteamNetworking(IntPtr client, int user, int pipe, string version);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr user);

        // ---- ISteamNetworking: the legacy P2P API ----

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_SendP2PPacket(
            IntPtr self, ulong steamIDRemote, byte[] data, uint cubData, int eP2PSendType, int nChannel);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_IsP2PPacketAvailable(
            IntPtr self, out uint pcubMsgSize, int nChannel);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_ReadP2PPacket(
            IntPtr self, byte[] pubDest, uint cubDest, out uint pcubMsgSize, out ulong psteamIDRemote, int nChannel);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(IntPtr self, ulong steamIDRemote);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(IntPtr self, ulong steamIDRemote);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_AllowP2PPacketRelay(IntPtr self, bool bAllow);

        [DllImport(Lib, CallingConvention = Cdecl)]
        internal static extern bool SteamAPI_ISteamNetworking_GetP2PSessionState(
            IntPtr self, ulong steamIDRemote, out P2PSessionState state);

        /// <summary>Matches Steam's P2PSessionState_t: four bytes of flags, then the route it settled on.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct P2PSessionState
        {
            internal byte ConnectionActive;
            internal byte Connecting;
            internal byte P2PSessionError;
            internal byte UsingRelay;
            internal int BytesQueuedForSend;
            internal int PacketsQueuedForSend;
            internal uint RemoteIP;
            internal ushort RemotePort;
        }

        /// <summary>EP2PSend. Steam fragments and orders the reliable ones for us, up to 1 MB.</summary>
        internal const int SendUnreliable = 0;
        internal const int SendUnreliableNoDelay = 1;
        internal const int SendReliable = 2;
        internal const int SendReliableWithBuffering = 3;
    }
}
