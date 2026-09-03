using System.IO;
using System.Text;
using KspMp.Shared.Codec;

namespace KspMp.Vessels
{
    /// <summary>ProtoVessel &lt;-&gt; deflated ConfigNode text, the wire format of vessel snapshots.</summary>
    public static class ProtoCodec
    {
        public static byte[] Serialize(ProtoVessel proto)
        {
            var node = new ConfigNode("VESSEL");
            proto.Save(node);
            var raw = Encoding.UTF8.GetBytes(ToText(node));
            return DeflateCodec.Compress(raw, 0, raw.Length);
        }

        public static ConfigNode ToNode(byte[] deflated)
        {
            if (deflated == null || deflated.Length == 0) return null;
            var raw = DeflateCodec.Decompress(deflated, 0, deflated.Length);
            var parsed = ConfigNode.Parse(Encoding.UTF8.GetString(raw));
            if (parsed == null) return null;
            return parsed.GetNode("VESSEL") ?? parsed;
        }

        public static ProtoVessel ToProto(byte[] deflated, global::Game game)
        {
            var node = ToNode(deflated);
            return node == null ? null : new ProtoVessel(node, game);
        }

        /// <summary>KSP's own text writer (private, publicized), so escaping matches what ConfigNode.Parse expects.</summary>
        public static string ToText(ConfigNode node)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    node.WriteNode(writer);
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
