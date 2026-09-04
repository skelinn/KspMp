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
        public bool IAmPilot(Guid vesselId) => Net.ClientId != 0 && PilotOf(vesselId) == Net.ClientId;

        /// <summary>What we may do with the active vessel: owner, co-pilot, or spectator.</summary>
        public string RoleText
        {
            get
            {
                if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.fetch == null) return "";
                var vessel = FlightGlobals.ActiveVessel;
                if (vessel == null) return "";
                if (Addon.Vessels.IsMine(vessel.id)) return IAmPilot(vessel.id) ? "Pilot" : IAmAboard(vessel.id) ? "Pilot (physics)" : "Controlling";
                if (IAmAboard(vessel.id)) return "Co-pilot" + (PilotOf(vessel.id) != 0 ? " of " + NameOf(PilotOf(vessel.id)) : "") + (SharedStickFor(vessel.id) ? ", shared stick" : ", actions only");
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
            Unhook();
            _roles.Clear();
            _inputs.Clear();
        }

        public override void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready)
            {
                Unhook();
                return;
            }
            var active = ActiveVesselOrNull;
            if (active == null)
            {
                Unhook();
                return;
            }
            var asOwner = Addon.Vessels.IsMine(active.id);
            if (_hooked != active || _hookedAsOwner != asOwner) Hook(active, asOwner);
        }

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

        public void SendStage(Guid vesselId)
        {
            Net.Send(MessageId.Stage, new StageMsg { VesselId = vesselId }, Channel.Control, Delivery.ReliableOrdered);
            ScreenMessages.PostScreenMessage("Staging (via " + NameOf(Addon.Vessels.OwnerOf(vesselId)) + ")", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        public void SendActionGroup(Guid vesselId, KSPActionGroup group, bool toggle, bool value)
        {
            Net.Send(MessageId.ActionGroup, new ActionGroupMsg { VesselId = vesselId, Group = (int)group, Toggle = toggle, Value = value }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void SendSasMode(Guid vesselId, int mode, bool enabled)
        {
            Net.Send(MessageId.SasMode, new SasModeMsg { VesselId = vesselId, Mode = mode, Enabled = enabled }, Channel.Control, Delivery.ReliableOrdered);
        }

        public void SendPartEvent(Guid vesselId, uint partFlightId, int moduleIndex, string eventName)
        {
            Net.Send(MessageId.PartEvent, new PartEventMsg { VesselId = vesselId, PartFlightId = partFlightId, ModuleIndex = moduleIndex, EventName = eventName }, Channel.Control, Delivery.ReliableOrdered);
            ScreenMessages.PostScreenMessage(eventName + " (via " + NameOf(Addon.Vessels.OwnerOf(vesselId)) + ")", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void OnRoles(NetDataReader body)
        {
            var msg = Envelope.Read<VesselRolesMsg>(body);
            _roles[msg.VesselId] = msg;
            var label = Addon.Vessels.TryGet(msg.VesselId, out var rv) ? rv.Label : msg.VesselId.ToString().Substring(0, 8);
            Log.Info("Roles for " + label + ": pilot " + (msg.PilotClientId == 0 ? "none" : NameOf(msg.PilotClientId)) + ", aboard " + (msg.AboardClientIds != null ? msg.AboardClientIds.Length : 0) + (IAmAboard(msg.VesselId) ? " (we are aboard as " + (IAmPilot(msg.VesselId) ? "pilot" : "co-pilot") + ")" : ""));
        }

        private void OnCtrlInput(NetDataReader body)
        {
            var msg = Envelope.Read<CtrlInputMsg>(body);
            if (_hooked == null || !_hookedAsOwner || msg.VesselId != _hooked.id) return;
            if (!_inputs.TryGetValue(msg.FromClientId, out var input)) _inputs[msg.FromClientId] = input = new RemoteInput();
            if (input.Msg.Seq != 0 && msg.Seq <= input.Msg.Seq && msg.Seq > input.Msg.Seq - 1000) return; // stale
            input.Msg = msg;
            input.ReceivedAt = Time.realtimeSinceStartup;
            InputsReceived++;
        }

        private void OnCtrlState(NetDataReader body)
        {
            _ownerState = Envelope.Read<CtrlInputMsg>(body);
            _ownerStateAt = Time.realtimeSinceStartup;
        }

        private Vessel OwnedActiveVessel(Guid vesselId)
        {
            var vessel = ActiveVesselOrNull;
            if (vessel == null || vessel.id != vesselId || !Addon.Vessels.IsMine(vesselId)) return null;
            return vessel;
        }

        private Vessel ActiveVesselOrNull => HighLogic.LoadedSceneIsFlight && FlightGlobals.fetch != null ? FlightGlobals.ActiveVessel : null;

        private void OnStage(NetDataReader body)
        {
            var msg = Envelope.Read<StageMsg>(body);
            var vessel = OwnedActiveVessel(msg.VesselId);
            if (vessel == null) return;
            Apply("stage by " + NameOf(msg.FromClientId), () => KSP.UI.Screens.StageManager.ActivateNextStage());
        }

        private void OnActionGroup(NetDataReader body)
        {
            var msg = Envelope.Read<ActionGroupMsg>(body);
            var vessel = OwnedActiveVessel(msg.VesselId);
            if (vessel == null) return;
            var group = (KSPActionGroup)msg.Group;
            Apply("action group " + group + " by " + NameOf(msg.FromClientId), () =>
            {
                if (msg.Toggle) vessel.ActionGroups.ToggleGroup(group);
                else vessel.ActionGroups.SetGroup(group, msg.Value);
            });
        }

        private void OnSasMode(NetDataReader body)
        {
            var msg = Envelope.Read<SasModeMsg>(body);
            var vessel = OwnedActiveVessel(msg.VesselId);
            if (vessel == null || vessel.Autopilot == null) return;
            Apply("SAS mode " + (VesselAutopilot.AutopilotMode)msg.Mode + " by " + NameOf(msg.FromClientId), () =>
            {
                if (msg.Enabled != vessel.ActionGroups[KSPActionGroup.SAS]) vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, msg.Enabled);
                if (msg.Enabled) vessel.Autopilot.SetMode((VesselAutopilot.AutopilotMode)msg.Mode);
            });
        }

        private void OnPartEvent(NetDataReader body)
        {
            var msg = Envelope.Read<PartEventMsg>(body);
            var vessel = OwnedActiveVessel(msg.VesselId);
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
