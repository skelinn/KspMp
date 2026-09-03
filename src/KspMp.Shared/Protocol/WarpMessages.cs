using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    public enum WarpMode : byte
    {
        /// <summary>On-rails warp (KSP TimeWarp.Modes.HIGH).</summary>
        Rails = 0,
        /// <summary>Physics warp (KSP TimeWarp.Modes.LOW).</summary>
        Physics = 1,
    }

    /// <summary>KSP's stock warp rate tables, indexed the way TimeWarp.SetRate expects.</summary>
    public static class WarpRates
    {
        public static readonly float[] Rails = { 1f, 5f, 10f, 50f, 100f, 1000f, 10000f, 100000f };
        public static readonly float[] Physics = { 1f, 2f, 3f, 4f };

        public static float Rate(WarpMode mode, int index)
        {
            var table = mode == WarpMode.Physics ? Physics : Rails;
            if (index < 0) index = 0;
            if (index >= table.Length) index = table.Length - 1;
            return table[index];
        }

        public static int MaxIndex(WarpMode mode) => (mode == WarpMode.Physics ? Physics : Rails).Length - 1;
    }

    /// <summary>
    /// A client's warp wish. DesiredIndex 0 means "I do not want to warp" (and cancels warp for everyone).
    /// MaxRailsIndex is the highest on-rails index KSP allows this client right now (altitude limit), -1 = unlimited.
    /// </summary>
    public struct WarpRequestMsg : INetSerializable
    {
        public WarpMode Mode;
        public int DesiredIndex;
        public int MaxRailsIndex;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Mode);
            w.Put(DesiredIndex);
            w.Put(MaxRailsIndex);
        }

        public void Deserialize(NetDataReader r)
        {
            Mode = (WarpMode)r.GetByte();
            DesiredIndex = r.GetInt();
            MaxRailsIndex = r.GetInt();
        }
    }

    /// <summary>The negotiated warp everyone runs at.</summary>
    public struct WarpStateMsg : INetSerializable
    {
        public WarpMode Mode;
        public int RateIndex;
        public float Rate;
        public double Ut;
        /// <summary>Client whose request set the rate (0 = nobody, running at 1x).</summary>
        public int RequesterClientId;
        /// <summary>Client whose altitude limit capped the rate (0 = none).</summary>
        public int LimitingClientId;

        public void Serialize(NetDataWriter w)
        {
            w.Put((byte)Mode);
            w.Put(RateIndex);
            w.Put(Rate);
            w.Put(Ut);
            w.Put(RequesterClientId);
            w.Put(LimitingClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            Mode = (WarpMode)r.GetByte();
            RateIndex = r.GetInt();
            Rate = r.GetFloat();
            Ut = r.GetDouble();
            RequesterClientId = r.GetInt();
            LimitingClientId = r.GetInt();
        }
    }
}
