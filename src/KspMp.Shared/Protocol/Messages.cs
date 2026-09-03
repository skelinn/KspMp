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
        public string ServerName;
        public double UniversalTime;
        public float TimeRate;
        /// <summary>True when this player has no Kerbal avatar yet and must claim one before entering the game.</summary>
        public bool NeedsAvatar;
        /// <summary>The player's avatar kerbal (empty when NeedsAvatar).</summary>
        public string AvatarKerbalName;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientId);
            w.Put(ServerName ?? string.Empty);
            w.Put(UniversalTime);
            w.Put(TimeRate);
            w.Put(NeedsAvatar);
            w.Put(AvatarKerbalName ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientId = r.GetInt();
            ServerName = r.GetString();
            UniversalTime = r.GetDouble();
            TimeRate = r.GetFloat();
            NeedsAvatar = r.GetBool();
            AvatarKerbalName = r.GetString();
        }
    }

    public struct SyncCompleteMsg : INetSerializable
    {
        public int Kerbals;
        public int Vessels;

        public void Serialize(NetDataWriter w)
        {
            w.Put(Kerbals);
            w.Put(Vessels);
        }

        public void Deserialize(NetDataReader r)
        {
            Kerbals = r.GetInt();
            Vessels = r.GetInt();
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

    public struct PlayerInfo : INetSerializable
    {
        public int ClientId;
        public Guid PlayerId;
        public string Name;
        public int PingMs;
        public string AvatarKerbalName;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientId);
            w.PutGuidRaw(PlayerId);
            w.Put(Name ?? string.Empty);
            w.Put(PingMs);
            w.Put(AvatarKerbalName ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientId = r.GetInt();
            PlayerId = r.GetGuidRaw();
            Name = r.GetString();
            PingMs = r.GetInt();
            AvatarKerbalName = r.GetString();
        }
    }

    /// <summary>Everyone currently online, including the receiver. Sent after Welcome and refreshed periodically.</summary>
    public struct PlayerListMsg : INetSerializable
    {
        public PlayerInfo[] Players;

        public void Serialize(NetDataWriter w)
        {
            var count = Players != null ? Players.Length : 0;
            w.Put((ushort)count);
            for (var i = 0; i < count; i++) Players[i].Serialize(w);
        }

        public void Deserialize(NetDataReader r)
        {
            var count = r.GetUShort();
            Players = new PlayerInfo[count];
            for (var i = 0; i < count; i++)
            {
                var p = new PlayerInfo();
                p.Deserialize(r);
                Players[i] = p;
            }
        }
    }

    public struct PlayerJoinedMsg : INetSerializable
    {
        public PlayerInfo Player;

        public void Serialize(NetDataWriter w) => Player.Serialize(w);
        public void Deserialize(NetDataReader r) => Player.Deserialize(r);
    }

    public struct PlayerLeftMsg : INetSerializable
    {
        public int ClientId;
        public string Name;
        public string Reason;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientId);
            w.Put(Name ?? string.Empty);
            w.Put(Reason ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientId = r.GetInt();
            Name = r.GetString();
            Reason = r.GetString();
        }
    }

    // ---- chat ----

    public struct ChatMsg : INetSerializable
    {
        /// <summary>0 = server notice. Clients send it as 0 too; the server fills in the sender.</summary>
        public int FromClientId;
        public string FromName;
        public string Text;

        public void Serialize(NetDataWriter w)
        {
            w.Put(FromClientId);
            w.Put(FromName ?? string.Empty);
            w.Put(Text ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            FromClientId = r.GetInt();
            FromName = r.GetString();
            Text = r.GetString();
        }
    }

    // ---- time ----

    public struct TimeSyncReqMsg : INetSerializable
    {
        public long ClientTicks;

        public void Serialize(NetDataWriter w) => w.Put(ClientTicks);
        public void Deserialize(NetDataReader r) => ClientTicks = r.GetLong();
    }

    /// <summary>Server clock snapshot. ClientTicks echoes a request (0 when broadcast unsolicited).</summary>
    public struct TimeSyncMsg : INetSerializable
    {
        public long ClientTicks;
        public long ServerTicks;
        public double UniversalTime;
        public float Rate;

        public void Serialize(NetDataWriter w)
        {
            w.Put(ClientTicks);
            w.Put(ServerTicks);
            w.Put(UniversalTime);
            w.Put(Rate);
        }

        public void Deserialize(NetDataReader r)
        {
            ClientTicks = r.GetLong();
            ServerTicks = r.GetLong();
            UniversalTime = r.GetDouble();
            Rate = r.GetFloat();
        }
    }
}
