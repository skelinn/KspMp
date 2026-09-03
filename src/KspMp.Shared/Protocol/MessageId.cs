namespace KspMp.Shared.Protocol
{
    /// <summary>
    /// Stable wire ids. Never renumber; append within the domain's block.
    /// Blocks: handshake 1-9, players 10-19, chat 20-29, time 100+, warp 120+, roster 200+, presence 300+,
    /// vessel 400+, authority 500+, control 600+, discrete actions 620+, crew 700+, docking 800+, editor 900+,
    /// scenario 1000+, mod channel 1100+.
    /// </summary>
    public enum MessageId : ushort
    {
        None = 0,

        // handshake
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        /// <summary>Server -> client: the initial roster, vessel and presence sync is complete.</summary>
        SyncComplete = 4,

        // players
        Ping = 10,
        Pong = 11,
        PlayerList = 12,
        PlayerJoined = 13,
        PlayerLeft = 14,

        // chat
        Chat = 20,

        // time
        TimeSyncReq = 100,
        TimeSync = 101,

        // warp
        WarpRequest = 120,
        WarpState = 121,

        // roster and avatars
        KerbalProto = 200,
        KerbalStatus = 201,
        KerbalRemoved = 202,
        AvatarClaim = 210,
        AvatarClaimResult = 211,

        // presence
        Presence = 300,

        // vessels
        VesselProto = 400,
        VesselRemove = 401,
        VesselState = 402,

        // physics authority
        AuthorityAssign = 500,
        AuthorityRequest = 501,
        AuthorityRelease = 502,
    }
}
