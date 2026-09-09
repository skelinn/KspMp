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

    /// <summary>The pilot lets everyone else aboard steer while the pilot's own stick is neutral. Pilot only.</summary>
    public struct ControlSetSharedStickMsg : INetSerializable
    {
        public Guid VesselId;
        public bool Enabled;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(Enabled);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            Enabled = r.GetBool();
        }
    }

    /// <summary>
    /// The pilot hands a vessel to another player. The server refuses unless that player is actually in flight on
    /// it: handing a vessel to somebody who is not there leaves nobody simulating it, which is worse than the
    /// wrong player simulating it (see the docking-authority section of docs/PLAN.md).
    /// </summary>
    public struct ControlGiveMsg : INetSerializable
    {
        public Guid VesselId;
        public int ToClientId;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(ToClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            ToClientId = r.GetInt();
        }
    }

    /// <summary>A co-pilot asks for the stick. Client -> server (FromClientId ignored) and server -> pilot (filled in).</summary>
    public struct ControlRequestMsg : INetSerializable
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

    /// <summary>The pilot says no to a <see cref="ControlRequestMsg"/>; relayed back to whoever asked.</summary>
    public struct ControlDeclineMsg : INetSerializable
    {
        public Guid VesselId;
        public int ToClientId;
        public int FromClientId;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(ToClientId);
            w.Put(FromClientId);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            ToClientId = r.GetInt();
            FromClientId = r.GetInt();
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
        /// <summary>The stage the pilot fired, so a copy fires the same one rather than whatever it thinks is next. -1 = next.</summary>
        public int Stage;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
            w.Put(Stage);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
            Stage = r.GetInt();
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
    /// <summary>A part-menu field (a thrust limiter slider, a fuel-flow toggle, a cycle) set to a value, as text.</summary>
    public struct PartFieldMsg : INetSerializable
    {
        public Guid VesselId;
        public int FromClientId;
        public uint PartFlightId;
        /// <summary>Index into the part's module list; -1 for a field on the part itself.</summary>
        public int ModuleIndex;
        public string FieldName;
        /// <summary>The value in invariant-culture text; the receiver converts it to the field's type.</summary>
        public string Value;

        public void Serialize(NetDataWriter w)
        {
            w.PutGuidRaw(VesselId);
            w.Put(FromClientId);
            w.Put(PartFlightId);
            w.Put(ModuleIndex);
            w.Put(FieldName ?? string.Empty);
            w.Put(Value ?? string.Empty);
        }

        public void Deserialize(NetDataReader r)
        {
            VesselId = r.GetGuidRaw();
            FromClientId = r.GetInt();
            PartFlightId = r.GetUInt();
            ModuleIndex = r.GetInt();
            FieldName = r.GetString();
            Value = r.GetString();
        }
    }

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
