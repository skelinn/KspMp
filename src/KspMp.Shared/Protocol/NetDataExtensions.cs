using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    public static class NetDataExtensions
    {
        // LiteNetLib's own Put(Guid)/GetGuid() change wire layout depending on the target framework
        // (raw 16 bytes with spans, length-prefixed otherwise). These always write exactly 16 raw bytes.

        public static void PutGuidRaw(this NetDataWriter writer, Guid guid)
        {
            writer.Put(guid.ToByteArray(), 0, 16);
        }

        public static Guid GetGuidRaw(this NetDataReader reader)
        {
            var bytes = new byte[16];
            reader.GetBytes(bytes, 0, 16);
            return new Guid(bytes);
        }
    }
}
