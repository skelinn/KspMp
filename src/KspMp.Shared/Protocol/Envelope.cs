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
    /// <summary>
    /// Byte blobs with a 32-bit length. LiteNetLib's own PutBytesWithLength writes a 16-bit length and copies
    /// only that many bytes, silently, so a vessel snapshot over 64 KiB (a big station) arrived truncated and
    /// never loaded anywhere.
    /// </summary>
    public static class NetBlob
    {
        public static void PutBlob(this NetDataWriter w, byte[] data)
        {
            var length = data != null ? data.Length : 0;
            w.Put(length);
            if (length > 0) w.Put(data, 0, length);
        }

        /// <summary>
        /// Nothing the mod sends comes near this. The length is read off the wire before anyone has been
        /// authenticated, so without a ceiling a few hundred bytes claiming a two-gigabyte blob would have the
        /// host allocating it. A craft that genuinely will not fit is a craft we cannot send anyway.
        /// </summary>
        public const int MaxBlobBytes = 32 * 1024 * 1024;

        public static byte[] GetBlob(this NetDataReader r)
        {
            var length = r.GetInt();
            if (length <= 0) return Array.Empty<byte>();
            if (length > MaxBlobBytes) throw new InvalidOperationException("A blob of " + length + " bytes is past the " + MaxBlobBytes + " byte limit");
            if (length > r.AvailableBytes) throw new InvalidOperationException("A blob of " + length + " bytes was announced but only " + r.AvailableBytes + " arrived");
            var data = new byte[length];
            r.GetBytes(data, length);
            return data;
        }
    }

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
                writer.PutBlob(packed);
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
            var packed = reader.GetBlob();
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
