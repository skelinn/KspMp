namespace KspMp.Shared.Protocol
{
    public static class ProtocolVersion
    {
        /// <summary>Bump whenever the wire format changes incompatibly. Server and client must match exactly.</summary>
        public const ushort Current = 1;
    }
}
