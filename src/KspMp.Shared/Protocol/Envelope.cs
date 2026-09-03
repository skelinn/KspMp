using System;
using KspMp.Shared.Codec;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    [Flags]
    public enum EnvelopeFlags : byte
    {
        None = 0,
        /// <summary>Body is a length-prefixed Deflate stream of the serialized message.</summary>
        Deflated = 1,
        /// <summary>A uint sequence number follows the flags (used by sequenced state streams).</summary>
        HasSeq = 2,
    }

    /// <summary>
    /// Wire envelope: ushort MessageId | byte EnvelopeFlags | [uint Seq] | body.
    /// </summary>
    public static class Envelope
    {
        public static void Write<T>(NetDataWriter writer, MessageId id, T body, EnvelopeFlags flags = EnvelopeFlags.None, uint seq = 0)
            where T : INetSerializable
        {
            writer.Reset();
            writer.Put((ushort)id);
            writer.Put((byte)flags);
            if ((flags & EnvelopeFlags.HasSeq) != 0) writer.Put(seq);

            if ((flags & EnvelopeFlags.Deflated) != 0)
            {
                var raw = new NetDataWriter();
                body.Serialize(raw);
                var packed = DeflateCodec.Compress(raw.Data, 0, raw.Length);
                writer.PutBytesWithLength(packed);
            }
            else
            {
                body.Serialize(writer);
            }
        }

        public static bool TryReadHeader(NetDataReader reader, out MessageId id, out EnvelopeFlags flags, out uint seq)
        {
            id = MessageId.None;
            flags = EnvelopeFlags.None;
            seq = 0;
            if (reader.AvailableBytes < 3) return false;
            id = (MessageId)reader.GetUShort();
            flags = (EnvelopeFlags)reader.GetByte();
            if ((flags & EnvelopeFlags.HasSeq) != 0)
            {
                if (reader.AvailableBytes < 4) return false;
                seq = reader.GetUInt();
            }
            return true;
        }

        /// <summary>Returns a reader positioned at the message body, inflating it if needed.</summary>
        public static NetDataReader OpenBody(NetDataReader reader, EnvelopeFlags flags)
        {
            if ((flags & EnvelopeFlags.Deflated) == 0) return reader;
            var packed = reader.GetBytesWithLength();
            var raw = DeflateCodec.Decompress(packed, 0, packed.Length);
            return new NetDataReader(raw);
        }

        public static T Read<T>(NetDataReader body) where T : INetSerializable, new()
        {
            var message = new T();
            message.Deserialize(body);
            return message;
        }
    }
}
