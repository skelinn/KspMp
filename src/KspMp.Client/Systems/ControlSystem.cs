using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Seat-based shared control. The physics owner (normally the pilot) merges co-pilot input in its fly-by-wire
    /// callback and streams the merged state back; co-pilots send their input and mirror the owner's state locally.
    /// Discrete actions from co-pilots (staging, action groups, SAS, part buttons) are relayed to the owner.
    /// </summary>
    public sealed class ControlSystem : SystemBase
    {
        private sealed class RemoteInput
        {
            public CtrlInputMsg Msg;
            public float ReceivedAt;
        }

        public const float HoldSeconds = 0.3f;
        public const float ActiveSendInterval = 1f / 30f;
        public const float IdleSendInterval = 0.5f;
        public const float StateSendInterval = 0.1f;

        private readonly Dictionary<Guid, VesselRolesMsg> _roles = new Dictionary<Guid, VesselRolesMsg>();
        private readonly Dictionary<int, RemoteInput> _inputs = new Dictionary<int, RemoteInput>();
        private readonly List<int> _stale = new List<int>();
        private Vessel _hooked;
        private bool _hookedAsOwner;
        private uint _seq;
        private float _nextInputSendAt;
        private float _nextStateSendAt;
        private float _lastThrottleSent = -1f;
        private float _throttleActiveUntil;
        private float _pilotThrottle = -1f;
        private float _pilotThrottleChangedAt;
        private CtrlInputMsg _ownerState;
        private float _ownerStateAt = -10f;
        private float _nextMergeLogAt;
        private bool _coPilotLocked;

        /// <summary>
        /// The axes a locked co-pilot must not move. Deliberately not ControlTypes.ALL_SHIP_CONTROLS: that also
        /// swallows the staging and action-group keys, and those are exactly what a co-pilot is still allowed to
        /// press - they are relayed to the pilot by the Harmony patches.
        /// </summary>
        private const ControlTypes CoPilotAxes = ControlTypes.PITCH | ControlTypes.YAW | ControlTypes.ROLL
                                                 | ControlTypes.THROTTLE | ControlTypes.LINEAR
                                                 | ControlTypes.WHEEL_STEER | ControlTypes.WHEEL_THROTTLE
                                                 | ControlTypes.THROTTLE_CUT_MAX;
        private const string CoPilotLockId = "KspMp.copilot";

        public ControlSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "Control";
        /// <summary>True while we apply a relayed action locally, so the patches let the call through.</summary>
        public static bool ApplyingRemoteAction { get; private set; }
        public int InputsSent { get; private set; }
        public int InputsReceived { get; private set; }
        public int ActionsApplied { get; private set; }

        public bool TryGetRoles(Guid vesselId, out VesselRolesMsg roles) => _roles.TryGetValue(vesselId, out roles);
        public int PilotOf(Guid vesselId) => _roles.TryGetValue(vesselId, out var r) ? r.PilotClientId : 0;
        public bool SharedStickFor(Guid vesselId) => _roles.TryGetValue(vesselId, out var r) && r.SharedStick;

        public bool IsAboard(Guid vesselId, int clientId)
        {
            if (!_roles.TryGetValue(vesselId, out var r) || r.AboardClientIds == null) return false;
            for (var i = 0; i < r.AboardClientIds.Length; i++) if (r.AboardClientIds[i] == clientId) return true;
            return false;
        }

        public bool IAmAboard(Guid vesselId) => IsAboard(vesselId, Net.ClientId);

        /// <summary>Is anyone besides us aboard? Then our actions on the vessel are worth echoing to them.</summary>
        public bool OthersAboard(Guid vesselId)
        {
            if (!_roles.TryGetValue(vesselId, out var r) || r.AboardClientIds == null) return false;
            for (var i = 0; i < r.AboardClientIds.Length; i++) if (r.AboardClientIds[i] != Net.ClientId) return true;
            return false;
        }
        public bool IAmPilot(Guid vesselId) => Net.ClientId != 0 && PilotOf(vesselId) == Net.ClientId;

        /// <summary>What we may do with the active vessel: owner, co-pilot, or spectator.</summary>
        public string RoleText
        {
            get
            {
                if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null) return "";
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return "";
                // "Pilot" is flying your own kerbal's craft; "Controlling" is an uncrewed probe you simulate.
                if (Addon.Vessels.IsMine(vessel.id)) return IAmAboard(vessel.id) ? "Pilot" : "Controlling";
                if (IAmAboard(vessel.id)) return "Co-pilot" + (PilotOf(vessel.id) != 0 ? " of " + NameOf(PilotOf(vessel.id)) : "") + (SharedStickFor(vessel.id) ? " (shared stick)" : " (locked)");
                return "Spectating";
            }
        }

        private string NameOf(int clientId) => Addon.Players.TryGet(clientId, out var p) ? p.Name : "#" + clientId;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.VesselRoles, OnRoles);
            Net.RegisterHandler(MessageId.CtrlInput, OnCtrlInput);
            Net.RegisterHandler(MessageId.CtrlState, OnCtrlState);
            Net.RegisterHandler(MessageId.Stage, OnStage);
            Net.RegisterHandler(MessageId.ActionGroup, OnActionGroup);
            Net.RegisterHandler(MessageId.SasMode, OnSasMode);
            Net.RegisterHandler(MessageId.PartEvent, OnPartEvent);
            Net.RegisterHandler(MessageId.PartField, OnPartField);
            Net.RegisterHandler(MessageId.StageSequence, OnStageSequence);
            GameEvents.StageManager.OnGUIStageSequenceModified.Add(OnStagingRearranged);
            Net.RegisterHandler(MessageId.ControlRequest, OnControlRequest);
            Net.RegisterHandler(MessageId.ControlDecline, OnControlDecline);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.VesselRoles, OnRoles);
            Net.UnregisterHandler(MessageId.CtrlInput, OnCtrlInput);
            Net.UnregisterHandler(MessageId.CtrlState, OnCtrlState);
            Net.UnregisterHandler(MessageId.Stage, OnStage);
            Net.UnregisterHandler(MessageId.ActionGroup, OnActionGroup);
            Net.UnregisterHandler(MessageId.SasMode, OnSasMode);
            Net.UnregisterHandler(MessageId.PartEvent, OnPartEvent);
            Net.UnregisterHandler(MessageId.PartField, OnPartField);
            Net.UnregisterHandler(MessageId.StageSequence, OnStageSequence);
            GameEvents.StageManager.OnGUIStageSequenceModified.Remove(OnStagingRearranged);
            Net.UnregisterHandler(MessageId.ControlRequest, OnControlRequest);
            Net.UnregisterHandler(MessageId.ControlDecline, OnControlDecline);
            Unhook();
            SetCoPilotLock(false);
            _roles.Clear();
            _inputs.Clear();
        }

        public override void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready)
            {
                Unhook();
                SetCoPilotLock(false);
                return;
            }
            var active = ActiveVesselOrNull;
            if (active == null)
            {
                Unhook();
                SetCoPilotLock(false);
                return;
            }
            if (_stagingDirtyAt >= 0 && Time.realtimeSinceStartup - _stagingDirtyAt >= StagingDebounceSeconds)
            {
                _stagingDirtyAt = -1f;
                SendStageSequence(ActiveVesselOrNull);
            }
            var asOwner = Addon.Vessels.IsMine(active.id);
            if (_hooked != active || _hookedAsOwner != asOwner) Hook(active, asOwner);
            // Exclusive control by default: a co-pilot's stick does nothing until the pilot shares it. The
            // discrete actions stay live, which is the whole point of not locking ALL_SHIP_CONTROLS.
            SetCoPilotLock(!asOwner && IAmAboard(active.id) && !SharedStickFor(active.id));
        }

        private void SetCoPilotLock(bool locked)
        {
            // Ask the lock stack rather than a cached flag: KSP clears every control lock when the flight UI
            // switches to docking mode (FlightUIModeController), which used to hand a locked co-pilot the
            // stick for the rest of the flight.
            var has = InputLockManager.GetControlLock(CoPilotLockId) != ControlTypes.None;
            _coPilotLocked = locked;
            if (locked && !has) InputLockManager.SetControlLock(CoPilotAxes, CoPilotLockId);
            else if (!locked && has) InputLockManager.RemoveControlLock(CoPilotLockId);
        }

        // ---- asking for, giving up and sharing the stick ----

        /// <summary>Hands our vessel to another player. The server refuses unless they are in flight on it.</summary>
        public void GiveControl(Guid vesselId, int toClientId)
        {
            if (!Addon.Vessels.IsMine(vesselId) || toClientId == 0 || toClientId == Net.ClientId) return;
            Net.Send(MessageId.ControlGive, new ControlGiveMsg { VesselId = vesselId, ToClientId = toClientId }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Offered control of " + LabelOf(vesselId) + " to " + NameOf(toClientId));
        }

        public void RequestControl(Guid vesselId)
        {
            if (Addon.Vessels.IsMine(vesselId)) return;
            Net.Send(MessageId.ControlRequest, new ControlRequestMsg { VesselId = vesselId }, Channel.Control, Delivery.ReliableOrdered);
            // The server grants at once when nobody is flying it (the owner is not aboard); only a pilot is asked.
            var pilot = PilotOf(vesselId);
            Addon.Notices.Post("control-asked-" + vesselId,
                pilot != 0 ? "Asked " + NameOf(pilot) + " for control of " + LabelOf(vesselId) : "Taking control of " + LabelOf(vesselId) + ": nobody is flying it",
                Ui.Theme.Ink, ttlSeconds: 8f);
        }

        public void DeclineControl(Guid vesselId, int toClientId)
        {
            Net.Send(MessageId.ControlDecline, new ControlDeclineMsg { VesselId = vesselId, ToClientId = toClientId }, Channel.Control, Delivery.ReliableOrdered);
            Addon.Notices.Dismiss("control-request-" + vesselId);
        }

        public void SetSharedStick(Guid vesselId, bool enabled)
        {
            if (!Addon.Vessels.IsMine(vesselId)) return;
            Net.Send(MessageId.ControlSetSharedStick, new ControlSetSharedStickMsg { VesselId = vesselId, Enabled = enabled }, Channel.Control, Delivery.ReliableOrdered);
        }

        private void OnControlRequest(NetDataReader body)
        {
            var msg = Envelope.Read<ControlRequestMsg>(body);
            var asker = msg.FromClientId;
            var key = "control-request-" + msg.VesselId;
            Addon.Notices.Post(key,
                NameOf(asker) + " asks to fly " + LabelOf(msg.VesselId),
                Ui.Theme.Accent,
                new[]
                {
                    new NoticeSystem.Action { Label = "Give control", Primary = true, OnClick = () => { GiveControl(msg.VesselId, asker); Addon.Notices.Dismiss(key); } },
                    new NoticeSystem.Action { Label = "Not now", OnClick = () => DeclineControl(msg.VesselId, asker) },
                }, ttlSeconds: 30f);
        }

        private void OnControlDecline(NetDataReader body)
        {
            var msg = Envelope.Read<ControlDeclineMsg>(body);
            Addon.Notices.Post("control-declined-" + msg.VesselId, NameOf(msg.FromClientId) + " is keeping the stick for now", Ui.Theme.Dim, ttlSeconds: 8f);
        }

        private string LabelOf(Guid vesselId) => Addon.Vessels.TryGet(vesselId, out var rv) ? rv.Label : vesselId.ToString().Substring(0, 8);

        private void Hook(Vessel vessel, bool asOwner)
        {
            Unhook();
            _hooked = vessel;
            _hookedAsOwner = asOwner;
            if (asOwner) vessel.OnFlyByWire += OwnerFlyByWire;
            else vessel.OnFlyByWire += CoPilotFlyByWire;
            _inputs.Clear();
            _lastThrottleSent = -1f;
            Log.Info("Control hook on " + vessel.GetDisplayName() + " as " + (asOwner ? "owner" : "co-pilot/spectator"));
        }

        private void Unhook()
        {
            if (_hooked == null) return;
            try
            {
                _hooked.OnFlyByWire -= OwnerFlyByWire;
                _hooked.OnFlyByWire -= CoPilotFlyByWire;
            }
            catch (Exception e)
            {
                Log.Exception("Unhooking fly-by-wire", e);
            }
            _hooked = null;
        }

        // ---- owner: merge co-pilot input, broadcast the merged state ----

        private void OwnerFlyByWire(FlightCtrlState st)
        {
            var vessel = _hooked;
            if (vessel == null) return;
            var now = Time.realtimeSinceStartup;
            if (Math.Abs(st.mainThrottle - _pilotThrottle) > 0.001f)
            {
                _pilotThrottle = st.mainThrottle;
                _pilotThrottleChangedAt = now;
            }

            var shared = SharedStickFor(vessel.id);
            if (_inputs.Count > 0)
            {
                _stale.Clear();
                foreach (var pair in _inputs)
                {
                    var input = pair.Value;
                    if (now - input.ReceivedAt > HoldSeconds) { _stale.Add(pair.Key); continue; }
                    if (!shared) continue;
                    var m = input.Msg;
                    // Co-pilot axes apply while the pilot leaves them neutral; the pilot always wins when moving them.
                    if ((m.Active & CtrlAxes.Pitch) != 0 && Mathf.Abs(st.pitch) < 0.01f) st.pitch = m.Pitch;
                    if ((m.Active & CtrlAxes.Yaw) != 0 && Mathf.Abs(st.yaw) < 0.01f) st.yaw = m.Yaw;
                    if ((m.Active & CtrlAxes.Roll) != 0 && Mathf.Abs(st.roll) < 0.01f) st.roll = m.Roll;
                    if ((m.Active & CtrlAxes.X) != 0 && Mathf.Abs(st.X) < 0.01f) st.X = m.X;
                    if ((m.Active & CtrlAxes.Y) != 0 && Mathf.Abs(st.Y) < 0.01f) st.Y = m.Y;
                    if ((m.Active & CtrlAxes.Z) != 0 && Mathf.Abs(st.Z) < 0.01f) st.Z = m.Z;
                    if ((m.Active & CtrlAxes.WheelSteer) != 0 && Mathf.Abs(st.wheelSteer) < 0.01f) st.wheelSteer = m.WheelSteer;
                    if ((m.Active & CtrlAxes.WheelThrottle) != 0 && Mathf.Abs(st.wheelThrottle) < 0.01f) st.wheelThrottle = m.WheelThrottle;
                    if ((m.Active & CtrlAxes.MainThrottle) != 0 && now - _pilotThrottleChangedAt > 0.5f)
                    {
                        st.mainThrottle = m.MainThrottle;
                        FlightInputHandler.state.mainThrottle = m.MainThrottle; // keep the sticky throttle in step
                        _pilotThrottle = m.MainThrottle;
                    }
                    if (now >= _nextMergeLogAt)
                    {
                        _nextMergeLogAt = now + 5f;
                        Log.Info("Merging input from " + NameOf(pair.Key) + ": " + m.Active + " pitch " + m.Pitch.ToString("F2") + " throttle " + m.MainThrottle.ToString("F2"));
                    }
                }
                foreach (var key in _stale) _inputs.Remove(key);
            }

            if (now >= _nextStateSendAt && _roles.TryGetValue(vessel.id, out var roles) && roles.AboardClientIds != null && roles.AboardClientIds.Length > (IAmAboard(vessel.id) ? 1 : 0))
            {
                _nextStateSendAt = now + StateSendInterval;
                Net.Send(MessageId.CtrlState, FromState(vessel.id, st, CtrlAxes.None), Channel.State, Delivery.Sequenced);
            }
        }

        // ---- co-pilot / spectator: send our input, mirror the owner's state ----

        private void CoPilotFlyByWire(FlightCtrlState st)
        {
            var vessel = _hooked;
            if (vessel == null) return;
            var now = Time.realtimeSinceStartup;
            if (IAmAboard(vessel.id))
            {
                var active = CtrlAxes.None;
                if (Mathf.Abs(st.pitch) > 0.01f) active |= CtrlAxes.Pitch;
                if (Mathf.Abs(st.yaw) > 0.01f) active |= CtrlAxes.Yaw;
                if (Mathf.Abs(st.roll) > 0.01f) active |= CtrlAxes.Roll;
                if (Mathf.Abs(st.X) > 0.01f) active |= CtrlAxes.X;
                if (Mathf.Abs(st.Y) > 0.01f) active |= CtrlAxes.Y;
                if (Mathf.Abs(st.Z) > 0.01f) active |= CtrlAxes.Z;
                if (Mathf.Abs(st.wheelSteer) > 0.01f) active |= CtrlAxes.WheelSteer;
                if (Mathf.Abs(st.wheelThrottle) > 0.01f) active |= CtrlAxes.WheelThrottle;
                if (_lastThrottleSent >= 0f && Mathf.Abs(st.mainThrottle - _lastThrottleSent) > 0.001f) _throttleActiveUntil = now + 0.5f;
                if (now < _throttleActiveUntil) active |= CtrlAxes.MainThrottle;
                _lastThrottleSent = st.mainThrottle;

                var interval = active != CtrlAxes.None ? ActiveSendInterval : IdleSendInterval;
                if (now >= _nextInputSendAt)
                {
                    _nextInputSendAt = now + interval;
                    var msg = FromState(vessel.id, st, active);
                    msg.Seq = ++_seq;
                    Net.Send(MessageId.CtrlInput, msg, Channel.State, Delivery.Sequenced);
                    InputsSent++;
                }
            }

            // Show what the owner's vessel is actually doing (throttle gauge, control surfaces, plumes).
            if (now - _ownerStateAt < 1f && _ownerState.VesselId == vessel.id)
            {
                st.pitch = _ownerState.Pitch; st.yaw = _ownerState.Yaw; st.roll = _ownerState.Roll;
                st.X = _ownerState.X; st.Y = _ownerState.Y; st.Z = _ownerState.Z;
                st.mainThrottle = _ownerState.MainThrottle;
                st.wheelSteer = _ownerState.WheelSteer; st.wheelThrottle = _ownerState.WheelThrottle;
            }
        }

        private static CtrlInputMsg FromState(Guid vesselId, FlightCtrlState st, CtrlAxes active) => new CtrlInputMsg
        {
            VesselId = vesselId,
            Active = active,
            Pitch = st.pitch, Yaw = st.yaw, Roll = st.roll,
            X = st.X, Y = st.Y, Z = st.Z,
            MainThrottle = st.mainThrottle, WheelSteer = st.wheelSteer, WheelThrottle = st.wheelThrottle,
            KillRot = st.killRot,
        };

        // ---- relayed discrete actions (sent by co-pilots, applied by the owner) ----

        public void SendStage(Guid vesselId, bool quiet = false, int stage = -1)
        {
            Net.Send(MessageId.Stage, new StageMsg { VesselId = vesselId, Stage = stage }, Channel.Control, Delivery.ReliableOrdered);
            if (!quiet) ScreenMessages.PostScreenMessage("Staging (via " + NameOf(Addon.Vessels.OwnerOf(vesselId)) + ")", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void SendActionGroup(Guid vesselId, KSPActionGroup group, bool toggle, bool value)
        {
            Net.Send(MessageId.ActionGroup, new ActionGroupMsg { VesselId = vesselId, Group = (int)group, Toggle = toggle, Value = value }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void SendSasMode(Guid vesselId, int mode, bool enabled)
        {
            Net.Send(MessageId.SasMode, new SasModeMsg { VesselId = vesselId, Mode = mode, Enabled = enabled }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void SendPartField(Guid vesselId, uint partFlightId, int moduleIndex, string fieldName, string value)
        {
            Net.Send(MessageId.PartField, new PartFieldMsg { VesselId = vesselId, PartFlightId = partFlightId, ModuleIndex = moduleIndex, FieldName = fieldName, Value = value }, Channel.Control, Delivery.ReliableOrdered);
        }

        /// <summary>A field value as it travels: invariant text, whatever its type.</summary>
        public static string FieldValueText(object value) =>
            value == null ? "" : value is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : value.ToString();

        private static object FieldValueFrom(string text, Type type)
        {
            if (type == typeof(string)) return text;
            if (type.IsEnum) return Enum.Parse(type, text, true);
            if (type == typeof(bool)) return bool.Parse(text);
            return Convert.ChangeType(text, type, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Sets a field on a part (or one of its modules), the way the part menu does, without a menu.</summary>
        private static bool SetField(Part part, int moduleIndex, string fieldName, string value)
        {
            var fields = moduleIndex >= 0 && moduleIndex < part.Modules.Count ? part.Modules[moduleIndex].Fields : moduleIndex < 0 ? part.Fields : null;
            var field = fields != null ? fields[fieldName] : null;
            if (field == null) return false;
            return fields.SetValue(fieldName, FieldValueFrom(value, field.FieldInfo.FieldType));
        }

        private void OnPartField(NetDataReader body)
        {
            var msg = Envelope.Read<PartFieldMsg>(body);
            var vessel = ActionTarget(msg.VesselId, msg.FromClientId, out var mirrored);
            if (vessel == null) return;
            Part part = null;
            for (var i = 0; i < vessel.parts.Count; i++)
                if (vessel.parts[i].flightID == msg.PartFlightId) { part = vessel.parts[i]; break; }
            if (part == null) { Log.Warn("Part " + msg.PartFlightId + " not found for field " + msg.FieldName); return; }
            Apply(msg.FieldName + " = " + msg.Value + " on " + part.partInfo.title + " by " + NameOf(msg.FromClientId), () =>
            {
                if (!SetField(part, msg.ModuleIndex, msg.FieldName, msg.Value)) throw new InvalidOperationException("field " + msg.FieldName + " not found");
                // The part menu sets symmetry twins too; the sender's message names only the part they clicked.
                if (part.symmetryCounterparts != null)
                    foreach (var twin in part.symmetryCounterparts)
                        if (twin != null) SetField(twin, msg.ModuleIndex, msg.FieldName, msg.Value);
            });
            if (!mirrored && Addon.Vessels.IsMine(vessel.id) && OthersAboard(vessel.id))
                SendPartField(vessel.id, msg.PartFlightId, msg.ModuleIndex, msg.FieldName, msg.Value);
        }

        public void SendPartEvent(Guid vesselId, uint partFlightId, int moduleIndex, string eventName, bool quiet = false)
        {
            Net.Send(MessageId.PartEvent, new PartEventMsg { VesselId = vesselId, PartFlightId = partFlightId, ModuleIndex = moduleIndex, EventName = eventName }, Channel.Control, Delivery.ReliableOrdered);
            if (!quiet) ScreenMessages.PostScreenMessage(eventName + " (via " + NameOf(Addon.Vessels.OwnerOf(vesselId)) + ")", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void OnRoles(NetDataReader body)
        {
            var msg = Envelope.Read<VesselRolesMsg>(body);
            var hadPilot = _roles.TryGetValue(msg.VesselId, out var before) ? before.PilotClientId : 0;
            _roles[msg.VesselId] = msg;
            var label = Addon.Vessels.TryGet(msg.VesselId, out var rv) ? rv.Label : msg.VesselId.ToString().Substring(0, 8);
            Log.Info("Roles for " + label + ": pilot " + (msg.PilotClientId == 0 ? "none" : NameOf(msg.PilotClientId)) + ", aboard " + (msg.AboardClientIds != null ? msg.AboardClientIds.Length : 0) + (IAmAboard(msg.VesselId) ? " (we are aboard as " + (IAmPilot(msg.VesselId) ? "pilot" : "co-pilot") + ")" : ""));
            if (msg.PilotClientId != hadPilot && msg.PilotClientId != 0 && IAmAboard(msg.VesselId))
                Addon.Notices.Post("pilot-" + msg.VesselId,
                    msg.PilotClientId == Net.ClientId ? "You are now pilot of " + label : NameOf(msg.PilotClientId) + " is now pilot of " + label,
                    msg.PilotClientId == Net.ClientId ? Ui.Theme.Accent : Ui.Theme.Ink, ttlSeconds: 10f);
        }

        private void OnCtrlInput(NetDataReader body)
        {
            var msg = Envelope.Read<CtrlInputMsg>(body);
            if (_hooked == null || !_hookedAsOwner || msg.VesselId != _hooked.id) return;
            if (!_inputs.TryGetValue(msg.FromClientId, out var input)) _inputs[msg.FromClientId] = input = new RemoteInput();
            if (input.Msg.Seq != 0 && (int)(msg.Seq - input.Msg.Seq) <= 0) return;   // stale or duplicate (wrap-safe)
            input.Msg = msg;
            input.ReceivedAt = Time.realtimeSinceStartup;
            InputsReceived++;
        }

        private void OnCtrlState(NetDataReader body)
        {
            _ownerState = Envelope.Read<CtrlInputMsg>(body);
            _ownerStateAt = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// The vessel an incoming action applies to, or null. Two directions come through here. A co-pilot's
        /// action on a vessel we simulate: we apply it, and the result streams back to them. And the pilot's own
        /// action on the vessel we sit in as co-pilot: we mirror it, so our copy of the rocket does what theirs
        /// just did. In the second case KSP may split pieces off our copy - a spent stage, an escape tower - and
        /// those are discarded rather than announced, because the owner's real ones arrive as snapshots.
        /// </summary>
        // ---- the staging column, rearranged in flight ----

        private const float StagingDebounceSeconds = 0.35f;
        private float _stagingDirtyAt = -1f;

        /// <summary>
        /// Somebody dragged the staging column about. Dragging fires this many times, so the column goes out
        /// once it settles. Applying a received column fires it too, which is why ApplyingRemoteAction is checked.
        /// </summary>
        private void OnStagingRearranged()
        {
            if (ApplyingRemoteAction || !HighLogic.LoadedSceneIsFlight) return;
            _stagingDirtyAt = Time.realtimeSinceStartup;
        }

        public void SendStageSequence(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null || !Net.IsConnected) return;
            var mine = Addon.Vessels.IsMine(vessel.id);
            // Ours: only worth sending when somebody else can see it. Theirs: only if we are aboard, and then
            // it goes to the pilot, whose column is the one that decides what fires.
            if (mine)
            {
                if (!OthersAboard(vessel.id) && !(Addon.Presence != null && Addon.Presence.OthersInFlight())) return;
            }
            else if (!Addon.Vessels.IsOwnedByOther(vessel.id) || !IAmAboard(vessel.id)) return;

            var ids = new uint[vessel.parts.Count];
            var stages = new int[vessel.parts.Count];
            for (var i = 0; i < vessel.parts.Count; i++)
            {
                ids[i] = vessel.parts[i].flightID;
                stages[i] = vessel.parts[i].inverseStage;
            }
            Net.Send(MessageId.StageSequence, new StageSequenceMsg
            {
                VesselId = vessel.id,
                CurrentStage = vessel.currentStage,
                PartFlightIds = ids,
                Stages = stages,
            }, Channel.Control, Delivery.ReliableOrdered);
            Log.Info("Sent the staging column of " + vessel.GetDisplayName() + " (" + ids.Length + " part(s), stage " + vessel.currentStage + ")");
        }

        private void OnStageSequence(NetDataReader body)
        {
            var msg = Envelope.Read<StageSequenceMsg>(body);
            var vessel = ActionTarget(msg.VesselId, msg.FromClientId, out var mirrored);
            if (vessel == null || msg.PartFlightIds == null) return;
            Apply("the staging column from " + NameOf(msg.FromClientId), () =>
            {
                var changed = 0;
                for (var i = 0; i < msg.PartFlightIds.Length; i++)
                {
                    for (var p = 0; p < vessel.parts.Count; p++)
                    {
                        var part = vessel.parts[p];
                        if (part.flightID != msg.PartFlightIds[i]) continue;
                        if (part.inverseStage != msg.Stages[i]) { part.inverseStage = msg.Stages[i]; changed++; }
                        break;
                    }
                }
                if (vessel.isActiveVessel)
                {
                    vessel.currentStage = msg.CurrentStage;
                    // Redraw the column from the part values we just set; without this the icons keep the
                    // old grouping and the next press looks like it fired the wrong stage.
                    if (KSP.UI.Screens.StageManager.Instance != null) KSP.UI.Screens.StageManager.Instance.SortIcons(true);
                }
                Log.Info("Staging column applied to " + vessel.GetDisplayName() + ": " + changed + " part(s) moved");
            });
            // The relayed column reached the pilot; everyone else aboard has heard nothing yet.
            if (!mirrored && Addon.Vessels.IsMine(vessel.id) && OthersAboard(vessel.id)) SendStageSequence(vessel);
        }

        private Vessel ActionTarget(Guid vesselId, int fromClientId, out bool mirrored)
        {
            mirrored = false;
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null) return null;
            var vessel = FlightGlobals.FindVessel(vesselId);
            if (vessel == null || !vessel.loaded) return null;
            if (Addon.Vessels.IsMine(vesselId)) return vessel.isActiveVessel ? vessel : null;   // ours to act on only at the controls
            if (fromClientId != 0 && Addon.Vessels.OwnerOf(vesselId) == fromClientId)
            {
                // The owner's action on their vessel, mirrored on our loaded copy - whether we sit in it or
                // watch it from outside. What comes off it is adopted as the owner's when their snapshot lands.
                mirrored = true;
                Addon.VesselProto.ExpectSplitOff(1f);
                return vessel;
            }
            return null;
        }

        private Vessel ActiveVesselOrNull => HighLogic.LoadedSceneIsFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;

        private void OnStage(NetDataReader body)
        {
            var msg = Envelope.Read<StageMsg>(body);
            var vessel = ActionTarget(msg.VesselId, msg.FromClientId, out var mirrored);
            if (vessel == null) return;
            var stage = msg.Stage;
            Apply((mirrored ? "stage " + stage + " fired by " : "stage by ") + NameOf(msg.FromClientId) + (vessel.isActiveVessel ? "" : " on their " + vessel.GetDisplayName()), () =>
            {
                // Mirroring: fire the very stage the pilot fired. Relaying a co-pilot's press: our own next stage
                // is the truth, since we are the one simulating this vessel.
                if (mirrored && !vessel.isActiveVessel)
                {
                    // The stage manager only works the active vessel; a copy we watch from outside is staged
                    // part by part, which is what the stage manager does underneath.
                    if (stage < 0) stage = vessel.currentStage - 1;
                    foreach (var part in vessel.parts.ToArray())
                        if (part != null && part.inverseStage == stage) part.force_activate();
                    if (vessel.currentStage > stage) vessel.currentStage = stage;
                }
                else if (mirrored && stage >= 0) KSP.UI.Screens.StageManager.ActivateStage(stage);
                else KSP.UI.Screens.StageManager.ActivateNextStage();
            });
        }

        private void OnActionGroup(NetDataReader body)
        {
            var msg = Envelope.Read<ActionGroupMsg>(body);
            var vessel = ActionTarget(msg.VesselId, msg.FromClientId, out _);
            if (vessel == null) return;
            var group = (KSPActionGroup)msg.Group;
            Apply("action group " + group + " by " + NameOf(msg.FromClientId), () =>
            {
                if (msg.Toggle) vessel.ActionGroups.ToggleGroup(group);
                else
                {
                    // KSP drops a change inside the group's cooldown window with only a log line; a mirrored
                    // change is the pilot's truth and must land.
                    try { vessel.ActionGroups.cooldownTimes[BaseAction.GetGroupIndex(group)] = 0.0; } catch (Exception) { }
                    vessel.ActionGroups.SetGroup(group, msg.Value);
                }
            });
        }

        private void OnSasMode(NetDataReader body)
        {
            var msg = Envelope.Read<SasModeMsg>(body);
            var vessel = ActionTarget(msg.VesselId, msg.FromClientId, out var mirroredSas);
            if (vessel == null || vessel.Autopilot == null) return;
            if (mirroredSas && !vessel.isActiveVessel) return;   // SAS only matters on the vessel at our controls
            Apply("SAS mode " + (VesselAutopilot.AutopilotMode)msg.Mode + " by " + NameOf(msg.FromClientId), () =>
            {
                if (msg.Enabled != vessel.ActionGroups[KSPActionGroup.SAS]) vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, msg.Enabled);
                if (msg.Enabled) vessel.Autopilot.SetMode((VesselAutopilot.AutopilotMode)msg.Mode);
            });
        }

        private void OnPartEvent(NetDataReader body)
        {
            var msg = Envelope.Read<PartEventMsg>(body);
            var vessel = ActionTarget(msg.VesselId, msg.FromClientId, out var mirrored);
            if (vessel == null) return;
            Part part = null;
            for (var i = 0; i < vessel.parts.Count; i++)
                if (vessel.parts[i].flightID == msg.PartFlightId) { part = vessel.parts[i]; break; }
            if (part == null) { Log.Warn("Part " + msg.PartFlightId + " not found for event " + msg.EventName); return; }
            Apply(msg.EventName + " on " + part.partInfo.title + " by " + NameOf(msg.FromClientId), () =>
            {
                BaseEvent evt = null;
                if (msg.ModuleIndex >= 0 && msg.ModuleIndex < part.Modules.Count) evt = part.Modules[msg.ModuleIndex].Events[msg.EventName];
                else evt = part.Events[msg.EventName];
                if (evt == null) throw new InvalidOperationException("event " + msg.EventName + " not found");
                evt.Invoke();
            });
            // A relayed event is invoked here directly, not through the part action button, so the button's
            // Harmony echo never sees it: without this the co-pilot who asked for it, and every other
            // co-pilot, watched nothing happen while the pilot's chute opened.
            if (!mirrored && Addon.Vessels.IsMine(vessel.id) && OthersAboard(vessel.id))
                SendPartEvent(vessel.id, msg.PartFlightId, msg.ModuleIndex, msg.EventName, quiet: true);
        }

        private void Apply(string what, Action action)
        {
            try
            {
                ApplyingRemoteAction = true;
                action();
                ActionsApplied++;
                Log.Info("Applied " + what);
                ScreenMessages.PostScreenMessage(what, 2f, ScreenMessageStyle.UPPER_LEFT);
            }
            catch (Exception e)
            {
                Log.Exception("Applying " + what, e);
            }
            finally
            {
                ApplyingRemoteAction = false;
            }
        }
    }
}
