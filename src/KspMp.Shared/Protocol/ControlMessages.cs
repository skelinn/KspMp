using System;
using LiteNetLib.Utils;

namespace KspMp.Shared.Protocol
{
    /// <summary>Who does what on a vessel, computed by the server from the vessel snapshot and the avatar owners.</summary>
    public struct VesselRolesMsg : INetSerializable
    {
        public Guid VesselId;
        /// <summary>Client whose avatar sits in the command seat (0 = none).</summary>
        public int PilotClientId;
        /// <summary>Every client with an avatar aboard, pilot included.</summary>
        public int[] AboardClientIds;
        public bool SharedStick;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(PilotClientId);
            var count = AboardClientIds != null ? AboardClientIds.Length : 0;
            w.Put((byte)count);
            for (var i = 0; i < count; i++) w.Put(AboardClientIds[i]);
            w.Put(SharedStick);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            PilotClientId = r.GetInt();
            var count = r.GetByte();
            AboardClientIds = new int[count];
            for (var i = 0; i < count; i++) AboardClientIds[i] = r.GetInt();
            SharedStick = r.GetBool();
        }
    }

    [Flags]
    public enum CtrlAxes : ushort
    {
        None = 0,
        Pitch = 1,
        Yaw = 2,
        Roll = 4,
        X = 8,
        Y = 16,
        Z = 32,
        MainThrottle = 64,
        WheelSteer = 128,
        WheelThrottle = 256,
    }

    /// <summary>
    /// Flight control input. Co-pilots send theirs to the owner (via the server); the owner sends the merged state
    /// back to everyone aboard so their local vessel shows the same control surfaces and throttle.
    /// </summary>
    public struct CtrlInputMsg : INetSerializable
    {
        public Guid VesselId;
        public int FromClientId;
        public uint Seq;
        /// <summary>Which axes the sender is actively moving (non-neutral input).</summary>
        public CtrlAxes Active;
        public float Pitch, Yaw, Roll, X, Y, Z, MainThrottle, WheelSteer, WheelThrottle;
        public bool KillRot;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
            w.Put(Seq);
            w.Put((ushort)Active);
            w.Put(Pitch); w.Put(Yaw); w.Put(Roll);
            w.Put(X); w.Put(Y); w.Put(Z);
            w.Put(MainThrottle); w.Put(WheelSteer); w.Put(WheelThrottle);
            w.Put(KillRot);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
            Seq = r.GetUInt();
            Active = (CtrlAxes)r.GetUShort();
            Pitch = r.GetFloat(); Yaw = r.GetFloat(); Roll = r.GetFloat();
            X = r.GetFloat(); Y = r.GetFloat(); Z = r.GetFloat();
            MainThrottle = r.GetFloat(); WheelSteer = r.GetFloat(); WheelThrottle = r.GetFloat();
            KillRot = r.GetBool();
        }
    }

    public struct StageMsg : INetSerializable
    {
        public Guid VesselId;
        public int FromClientId;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
        }
    }

    public struct ActionGroupMsg : INetSerializable
    {
        public Guid VesselId;
        public int FromClientId;
        public int Group;
        public bool Toggle;
        public bool Value;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
            w.Put(Group);
            w.Put(Toggle);
            w.Put(Value);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
            Group = r.GetInt();
            Toggle = r.GetBool();
            Value = r.GetBool();
        }
    }

    public struct SasModeMsg : INetSerializable
    {
        public Guid VesselId;
        public int FromClientId;
        public int Mode;
        public bool Enabled;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
            w.Put(Mode);
            w.Put(Enabled);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
            Mode = r.GetInt();
            Enabled = r.GetBool();
        }
    }

    /// <summary>A part right-click button (BaseEvent) pressed by someone aboard who is not the physics owner.</summary>
    public struct PartEventMsg : INetSerializable
    {
        public Guid VesselId;
        public int FromClientId;
        public uint PartFlightId;
        public int ModuleIndex;
        public string EventName;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
            w.Put(PartFlightId);
            w.Put(ModuleIndex);
            w.Put(EventName ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
            PartFlightId = r.GetUInt();
            ModuleIndex = r.GetInt();
            EventName = r.GetString();
        }
    }
}
