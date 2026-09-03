namespace KspMp.Shared.Protocol
{
    /// <summary>Delivery guarantees, mapped 1:1 onto the transport (LiteNetLib DeliveryMethod).</summary>
    public enum Delivery : byte
    {
        /// <summary>Reliable and in order. Handshake, events, protos.</summary>
        ReliableOrdered = 0,
        /// <summary>Reliable, any order.</summary>
        ReliableUnordered = 1,
        /// <summary>Unreliable but never delivered out of order (older packets dropped). Vessel/control state.</summary>
        Sequenced = 2,
        /// <summary>Fire and forget. Pings.</summary>
        Unreliable = 3,
    }

    /// <summary>Independent ordering domains so a big proto never delays a control packet.</summary>
    public enum Channel : byte
    {
        Control = 0,
        State = 1,
        Bulk = 2,
        ChatMod = 3,
    }
}
