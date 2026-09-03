using System;
using System.Collections.Generic;
using KspMp.Shared.Protocol;
using LiteNetLib.Utils;
using UnityEngine;

namespace KspMp.Systems
{
    /// <summary>
    /// Keeps an estimate of the server's universal time. M1: measure and log; hard-correct the local UT outside
    /// flight when it drifts. M3 adds warp negotiation and gentle in-flight skewing.
    /// </summary>
    public sealed class TimeSyncSystem : SystemBase
    {
        private struct Sample
        {
            public long LocalTicksAtServerTime;
            public double UniversalTime;
            public float Rate;
            public double RttMs;
        }

        private const int MaxSamples = 8;
        /// <summary>Outside flight, a drift bigger than this snaps the local UT to the server's.</summary>
        public const double HardCorrectionThresholdSeconds = 1.0;

        private readonly List<Sample> _samples = new List<Sample>();
        private Sample _best;
        private float _nextRequestAt;
        private float _nextLogAt;

        public TimeSyncSystem(KspMpAddon addon) : base(addon) { }

        public override string Name => "TimeSync";
        public bool HasSync { get; private set; }
        public double RttMs => _best.RttMs;
        public float Rate => HasSync ? _best.Rate : 1f;
        public int Corrections { get; private set; }

        /// <summary>Best estimate of the server's UT right now.</summary>
        public double ServerUt => HasSync ? _best.UniversalTime + (DateTime.UtcNow.Ticks - _best.LocalTicksAtServerTime) / 1e7 * _best.Rate : 0;

        /// <summary>Local UT minus server UT, in seconds (0 when not in a game).</summary>
        public double DriftSeconds => HasSync && Planetarium.fetch != null && HighLogic.LoadedSceneIsGame ? Planetarium.GetUniversalTime() - ServerUt : 0;

        protected override void OnActivate()
        {
            Net.RegisterHandler(MessageId.TimeSync, OnTimeSync);
            _samples.Clear();
            HasSync = false;
            _nextRequestAt = 0f;
            _nextLogAt = Time.realtimeSinceStartup + 10f;
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.TimeSync, OnTimeSync);
            HasSync = false;
            _samples.Clear();
        }

        public override void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now >= _nextRequestAt)
            {
                _nextRequestAt = now + 1f;
                Net.Send(MessageId.TimeSyncReq, new TimeSyncReqMsg { ClientTicks = DateTime.UtcNow.Ticks }, Channel.State, Delivery.Unreliable);
            }

            if (!HasSync || Planetarium.fetch == null || !HighLogic.LoadedSceneIsGame) return;
            var drift = DriftSeconds;
            if (!HighLogic.LoadedSceneIsFlight && Math.Abs(drift) > HardCorrectionThresholdSeconds)
            {
                Planetarium.SetUniversalTime(ServerUt);
                Corrections++;
                Log.Info("UT snapped to server time (drift was " + drift.ToString("F3") + " s)");
            }
            if (now >= _nextLogAt)
            {
                _nextLogAt = now + 10f;
                Log.Info("UT drift " + (drift * 1000).ToString("F0") + " ms (rtt " + RttMs.ToString("F0") + " ms, rate " + Rate + "x, server UT " + ServerUt.ToString("F1") + ")");
            }
        }

        private void OnTimeSync(NetDataReader body)
        {
            var msg = Envelope.Read<TimeSyncMsg>(body);
            var now = DateTime.UtcNow.Ticks;
            var rttMs = msg.ClientTicks != 0 ? (now - msg.ClientTicks) / 1e4 : Net.PingMs;
            if (rttMs < 0) rttMs = 0;
            var sample = new Sample
            {
                LocalTicksAtServerTime = now - (long)(rttMs * 1e4 / 2),
                UniversalTime = msg.UniversalTime,
                Rate = msg.Rate,
                RttMs = rttMs,
            };

            if (HasSync && Math.Abs(msg.Rate - _best.Rate) > 0.001f) _samples.Clear(); // rate changed: old samples are stale
            _samples.Add(sample);
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);

            // Lowest round trip gives the most accurate offset.
            _best = _samples[0];
            for (var i = 1; i < _samples.Count; i++)
                if (_samples[i].RttMs < _best.RttMs) _best = _samples[i];
            HasSync = true;
        }
    }
}
