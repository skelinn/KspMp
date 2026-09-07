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
        /// <summary>In flight, drift beyond this snaps UT; below it (and above the dead band) Time.timeScale is skewed at 1x.</summary>
        public const double FlightHardCorrectionSeconds = 3.5;
        public const double SkewDeadBandSeconds = 0.025;

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
            _measuredRttMs = -1;
            _nextRequestAt = 0f;
            _nextLogAt = Time.realtimeSinceStartup + 10f;
        }

        protected override void OnDeactivate()
        {
            Net.UnregisterHandler(MessageId.TimeSync, OnTimeSync);
            HasSync = false;
            _samples.Clear();
            ResetSkew();
        }

        private bool _skewing;
        private float _nextSnapAllowedAt;

        private void ResetSkew()
        {
            if (!_skewing) return;
            _skewing = false;
            // Always put the scale back: physics warp owns timeScale (rate x), rails warp and 1x mean 1. Leaving
            // it skewed because a warp began the same frame stranded the client at 0.85x for the whole warp.
            Time.timeScale = TimeWarp.fetch != null && TimeWarp.WarpMode == TimeWarp.Modes.LOW ? TimeWarp.CurrentRate : 1f;
        }

        /// <summary>Last round trip actually measured (request -> reply); pushed samples borrow it.</summary>
        private double _measuredRttMs = -1;

        /// <summary>
        /// The server changed the shared rate. Its warp state carries the UT it switched at, which is a better
        /// sample than anything older: until now the estimate kept extrapolating at the old rate until the next
        /// time sample arrived, which at 100000x was tens of thousands of seconds of drift for half a second.
        /// </summary>
        public void OnWarpState(double ut, float rate)
        {
            if (ut <= 0) return;
            var now = DateTime.UtcNow.Ticks;
            var rttMs = _measuredRttMs >= 0 ? _measuredRttMs : Net.PingMs * 2.0;
            _samples.Clear();
            var sample = new Sample { LocalTicksAtServerTime = now - (long)(rttMs * 1e4 / 2), UniversalTime = ut, Rate = rate, RttMs = rttMs };
            _samples.Add(sample);
            _best = sample;
            HasSync = true;
        }

        public override void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now >= _nextRequestAt)
            {
                _nextRequestAt = now + 1f;
                Net.Send(MessageId.TimeSyncReq, new TimeSyncReqMsg { ClientTicks = DateTime.UtcNow.Ticks }, Channel.State, Delivery.Unreliable);
            }

            if (!HasSync || Planetarium.fetch == null || !HighLogic.LoadedSceneIsGame)
            {
                ResetSkew();
                return;
            }
            var drift = DriftSeconds;
            var threshold = HighLogic.LoadedSceneIsFlight ? FlightHardCorrectionSeconds : HardCorrectionThresholdSeconds;
            var localRate = TimeWarp.fetch != null ? TimeWarp.CurrentRate : 1f;
            var rateMismatch = Math.Abs(localRate - Rate) > 0.01f;
            // The thresholds are wall-clock seconds: at 1000x a few milliseconds of estimation error is several
            // seconds of UT, and snapping on that yanked every on-rails vessel along its orbit once a second.
            var driftWall = drift / Math.Max(1f, Rate);
            if (Math.Abs(driftWall) > threshold)
            {
                // With a rate mismatch snapping cannot help (the drift comes right back); do it rarely and say why.
                if (now < _nextSnapAllowedAt) return;
                _nextSnapAllowedAt = now + (rateMismatch ? 5f : 1f);
                Planetarium.SetUniversalTime(ServerUt);
                Corrections++;
                ResetSkew();
                Log.Info("UT snapped to server time (drift was " + drift.ToString("F3") + " s" + (rateMismatch ? ", local warp " + localRate + "x vs server " + Rate + "x" : "") + ")");
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                // Gentle catch-up at 1x: run a little faster or slower until the drift is inside the dead band.
                var warping = TimeWarp.fetch != null && TimeWarp.CurrentRate != 1f;
                if (!warping && Math.Abs(drift) > SkewDeadBandSeconds)
                {
                    Time.timeScale = Mathf.Clamp(Mathf.Pow(2f, -(float)drift), 0.85f, 1.2f);
                    _skewing = true;
                }
                else ResetSkew();
            }
            else ResetSkew();   // not in flight and inside the threshold: nothing to skew for
            if (now >= _nextLogAt)
            {
                _nextLogAt = now + 10f;
                Log.Info("UT drift " + (drift * 1000).ToString("F0") + " ms (rtt " + RttMs.ToString("F0") + " ms, server rate " + Rate + "x, KSP warp " + (TimeWarp.fetch != null ? TimeWarp.CurrentRate : 1f) + "x, timeScale " + Time.timeScale.ToString("F2") + ", server UT " + ServerUt.ToString("F1") + ")");
            }
        }

        private void OnTimeSync(NetDataReader body)
        {
            var msg = Envelope.Read<TimeSyncMsg>(body);
            var now = DateTime.UtcNow.Ticks;
            // A reply to our request carries a measured round trip. The server also pushes samples on its own;
            // those used to claim a round trip of "the transport's ping", which Steam and the loopback report as
            // zero, so they always won the lowest-round-trip pick and latency was never compensated at all.
            double rttMs;
            if (msg.ClientTicks != 0)
            {
                rttMs = (now - msg.ClientTicks) / 1e4;
                if (rttMs < 0) rttMs = 0;
                _measuredRttMs = _measuredRttMs < 0 ? rttMs : Math.Min(_measuredRttMs * 0.9 + rttMs * 0.1, rttMs + 50);
            }
            else rttMs = _measuredRttMs >= 0 ? _measuredRttMs : Net.PingMs * 2.0;
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
