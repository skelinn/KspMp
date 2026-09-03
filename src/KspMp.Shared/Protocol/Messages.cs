using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    // ---- handshake ----

    public struct HelloMsg : INetSerializable
    {
        public ushort ProtocolVersion;
        public string ModVersion;
        public Guid PlayerId;
        public string PlayerName;
        public string KspVersion;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ProtocolVersion);
            w.Put(ModVersion ?? string.Empty);
            w.PutGuidRaw(PlayerId);
            w.Put(PlayerName ?? string.Empty);
            w.Put(KspVersion ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            ProtocolVersion = r.GetUShort();
            ModVersion = r.GetString();
            PlayerId = r.GetGuidRaw();
            PlayerName = r.GetString();
            KspVersion = r.GetString();
        }
    }

    public struct WelcomeMsg : INetSerializable
    {
        public int ClientId;
        public double UniversalTime;
        public bool NeedsAvatar;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientId);
            w.Put(UniversalTime);
            w.Put(NeedsAvatar);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientId = r.GetInt();
            UniversalTime = r.GetDouble();
            NeedsAvatar = r.GetBool();
        }
    }

    public struct RejectMsg : INetSerializable
    {
        public string Reason;

        public void Serialize(NetDataWriter w) => w.Put(Reason ?? string.Empty);
        public void Deserialize(NetDataReader r) => Reason = r.GetString();
    }

    // ---- players ----

    public struct PingMsg : INetSerializable
    {
        public long ClientTicks;

        public void Serialize(NetDataWriter w) => w.Put(ClientTicks);
        public void Deserialize(NetDataReader r) => ClientTicks = r.GetLong();
    }

    public struct PongMsg : INetSerializable
    {
        public long ClientTicks;
        public long ServerTicks;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientTicks);
            w.Put(ServerTicks);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientTicks = r.GetLong();
            ServerTicks = r.GetLong();
        }
    }
}
