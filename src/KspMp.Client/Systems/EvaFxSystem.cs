using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// A kerbal's jetpack, seen from another machine.
    ///
    /// A remote kerbal is a frozen replica: its controller is switched off so that it holds the pose its
    /// owner sends instead of walking and flailing on its own (see VesselImmortal). That also switches off the
    /// code that lights the jetpack plumes and plays the RCS hiss, which is why a friend flying past on EVA
    /// was silent and dark. So the owner sends what the jetpack is doing, ten times a second while it is out,
    /// and every other machine lights the same twelve effect groups KSP would (KerbalEVA.cs:12017-12040).
    /// </summary>
    public sealed class EvaFxSystem : SystemBase
    {
        public const float IntervalSeconds = 0.1f;

        private float _nextSendAt;
        private readonly Dictionary<Guid, EvaFxMsg> _lastSent = new Dictionary<Guid, EvaFxMsg>();
        private readonly Dictionary<Guid, float> _lastSentAt = new Dictionary<Guid, float>();
        private float _nextPruneAt;

        public EvaFxSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "EvaFx";
        public int Sent { get; private set; }
        public int Applied { get; private set; }

        public override bool ShouldRun(GameScenes scene, bool connected) => connected && scene == GameScenes.FLIGHT;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.EvaFx, OnEvaFx);
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.EvaFx, OnEvaFx);
            _lastSent.Clear();
            _lastSentAt.Clear();
        }

        public override void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !Net.IsConnected) return;
            var now = Time.realtimeSinceStartup;
            if (now >= _nextPruneAt)
            {
                _nextPruneAt = now + 60f;
                foreach (var id in new List<Guid>(_lastSent.Keys))
                    if (FlightGlobals.FindVessel(id) == null) { _lastSent.Remove(id); _lastSentAt.Remove(id); }
            }
            if (now < _nextSendAt) return;
            _nextSendAt = now + IntervalSeconds;

            var loaded = FlightGlobals.VesselsLoaded;
            for (var i = 0; i < loaded.Count; i++)
            {
                var vessel = loaded[i];
                if (vessel == null || !vessel.isEVA || vessel.evaController == null || !Addon.Vessels.IsMine(vessel.id)) continue;
                var msg = Capture(vessel);
                // Unchanged: say so again once a second anyway. The stream is unreliable and sequenced, and a
                // lost "thrust off" left the plume lit on every other machine for good.
                var stale = !_lastSentAt.TryGetValue(vessel.id, out var at) || now - at > 1f;
                if (!stale && _lastSent.TryGetValue(vessel.id, out var last) && Same(last, msg)) continue;
                _lastSent[vessel.id] = msg;
                _lastSentAt[vessel.id] = now;
                Net.Send(MessageId.EvaFx, msg, Channel.State, Delivery.Sequenced);
                Sent++;
            }
        }

        private static EvaFxMsg Capture(Vessel vessel)
        {
            var eva = vessel.evaController;
            var msg = new EvaFxMsg { VesselId = vessel.id };
            if (eva.JetpackDeployed) msg.Flags |= EvaFxMsg.JetpackDeployed;
            if (eva.Fuel > 0.0) msg.Flags |= EvaFxMsg.HasFuel;
            if (eva.isRagdoll) msg.Flags |= EvaFxMsg.Ragdoll;
            if (!eva.JetpackDeployed || eva.isRagdoll || eva.Fuel <= 0.0) return msg;
            // The same two vectors KSP feeds its effect groups, in the kerbal's own frame.
            var inverse = Quaternion.Inverse(eva.transform.rotation);
            var lin = inverse * eva.packLinear;
            var rot = inverse * Vector3.ClampMagnitude(eva.cmdRot * (eva.thrustPercentage * 0.01f), 1f);
            msg.LinX = Scale(lin.x); msg.LinY = Scale(lin.y); msg.LinZ = Scale(lin.z);
            msg.RotX = Scale(rot.x); msg.RotY = Scale(rot.y); msg.RotZ = Scale(rot.z);
            return msg;
        }

        private static sbyte Scale(float v) => (sbyte)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 127f);
        private static float Unscale(sbyte v) => v / 127f;

        private static bool Same(EvaFxMsg a, EvaFxMsg b) =>
            a.Flags == b.Flags && a.LinX == b.LinX && a.LinY == b.LinY && a.LinZ == b.LinZ && a.RotX == b.RotX && a.RotY == b.RotY && a.RotZ == b.RotZ;

        private void OnEvaFx(NetDataReader body)
        {
            var msg = Envelope.Read<EvaFxMsg>(body);
            if (Addon.Vessels.IsMine(msg.VesselId)) return;
            var vessel = FlightGlobals.FindVessel(msg.VesselId);
            if (vessel == null || !vessel.loaded || !vessel.isEVA) return;
            var eva = vessel.evaController;
            if (eva == null) return;
            try
            {
                var deployed = (msg.Flags & EvaFxMsg.JetpackDeployed) != 0;
                if (eva.JetpackDeployed != deployed)
                {
                    eva.ToggleJetpack(deployed);
                    Log.Info("Jetpack of " + vessel.GetDisplayName() + (deployed ? " deployed" : " stowed") + " (its owner said so)");
                }
                var thrusting = deployed && (msg.Flags & EvaFxMsg.HasFuel) != 0 && (msg.Flags & EvaFxMsg.Ragdoll) == 0;
                var lin = thrusting ? new Vector3(Unscale(msg.LinX), Unscale(msg.LinY), Unscale(msg.LinZ)) : Vector3.zero;
                var rot = thrusting ? new Vector3(Unscale(msg.RotX), Unscale(msg.RotY), Unscale(msg.RotZ)) : Vector3.zero;
                Light(eva.xPos, lin.x, eva.linFXLatch); Light(eva.xNeg, -lin.x, eva.linFXLatch);
                Light(eva.yPos, lin.y, eva.linFXLatch); Light(eva.yNeg, -lin.y, eva.linFXLatch);
                Light(eva.zPos, lin.z, eva.linFXLatch); Light(eva.zNeg, -lin.z, eva.linFXLatch);
                Light(eva.PitchPos, rot.x, eva.rotFXLatch); Light(eva.PitchNeg, -rot.x, eva.rotFXLatch);
                Light(eva.YawPos, rot.y, eva.rotFXLatch); Light(eva.YawNeg, -rot.y, eva.rotFXLatch);
                Light(eva.RollPos, rot.z, eva.rotFXLatch); Light(eva.RollNeg, -rot.z, eva.rotFXLatch);
                Applied++;
            }
            catch (Exception e)
            {
                Log.Exception("Lighting the jetpack of " + vessel.GetDisplayName(), e);
            }
        }

        /// <summary>One thruster group, driven exactly as KerbalEVA drives its own: on above the latch, at this power.</summary>
        private static void Light(FXGroup group, float power, float latch)
        {
            if (group == null) return;
            group.SetLatch(power > latch);
            group.SetPowerLatch(power);
            if (power <= latch) group.Unlatch();
        }
    }
}
