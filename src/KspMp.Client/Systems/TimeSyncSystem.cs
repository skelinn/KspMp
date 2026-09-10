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
        /// <summary>The shared slow-motion factor the server is running: 1 unless somebody cannot keep up.</summary>
        public float Throttle { get; private set; } = 1f;
        /// <summary>What our own game managed lately, per second of real time. 1 when it is keeping up.</summary>
        public float AchievedRate { get; private set; } = 1f;

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
            Throttle = 1f;          // never leave a disconnected game in slow motion
            AchievedRate = 1f;
            _rateReal = -1f;
            ResetSkew();
        }

        private bool _skewing;
        private float _nextSnapAllowedAt;
        private float _nextSnapLogAt;

        /// <summary>What Time.timeScale should read with no drift correction on top: physics warp owns it, and the shared brake divides it.</summary>
        private float BaseScale => (TimeWarp.fetch != null && TimeWarp.WarpMode == TimeWarp.Modes.LOW ? TimeWarp.CurrentRate : 1f) * Throttle;

        private void ResetSkew()
        {
            // Always put the scale back: physics warp owns timeScale (rate x), rails warp and 1x mean 1. Leaving
            // it skewed because a warp began the same frame stranded the client at 0.85x for the whole warp.
            // The shared brake is part of the base, so a game that is keeping up still runs at everyone else's
            // pace rather than racing ahead and being snapped back.
            var target = BaseScale;
            if (!_skewing && Math.Abs(Time.timeScale - target) < 0.001f) return;
            _skewing = false;
            Time.timeScale = target;
        }

        private double _rateUt;
        private float _rateReal = -1f;
        private int _rateCorrections;

        /// <summary>
        /// How much game time this machine managed per second of real time since the last look. A heavy craft
        /// can drop KSP well under real time, and the shared clock then runs away from it: the drift grew,
        /// the local UT was snapped forward every few seconds, and every vessel jumped along its orbit each
        /// time. Reporting it lets the server hold the whole timeline to the slowest game instead.
        ///
        /// Measured in flight at 1x only, over at least three seconds, and thrown away if the window contained
        /// a snap or a scene load - during a loading screen no game time passes at all, which would otherwise
        /// read as a machine that had stopped. Under warp the clock is not physics-bound and says nothing.
        /// </summary>
        private float MeasureAchievedRate(float now)
        {
            var warping = TimeWarp.fetch != null && TimeWarp.CurrentRateIndex != 0;
            var running = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && Planetarium.fetch != null && HasSync && !warping;
            if (!running)
            {
                _rateReal = -1f;
                AchievedRate = 1f;
                return 1f;
            }
            var ut = Planetarium.GetUniversalTime();
            if (_rateReal < 0f)
            {
                _rateUt = ut;
                _rateReal = now;
                _rateCorrections = Corrections;
                return AchievedRate;
            }
            var real = now - _rateReal;
            if (real < 3f) return AchievedRate;

            var advanced = ut - _rateUt;
            // What the game was asked to run at over the window; timeScale is our own skew and the shared brake.
            var asked = real * (Time.timeScale > 0.01f ? Time.timeScale : 1f);
            var snapped = Corrections != _rateCorrections;
            _rateUt = ut;
            _rateReal = now;
            _rateCorrections = Corrections;
            if (snapped || asked <= 0 || advanced < 0) return AchievedRate;

            var achieved = Mathf.Clamp((float)(advanced / asked), 0.05f, 1f);
            // Down at once, up gently: one good window should not lift the brake off a machine that is still
            // struggling, but a machine that has stopped struggling should not hold everyone back for long.
            AchievedRate = achieved < AchievedRate ? achieved : Mathf.Min(1f, AchievedRate + 0.1f);
            return AchievedRate;
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

        private void OnThrottle(float throttle)
        {
            if (throttle <= 0f || throttle > 1f) throttle = 1f;
            if (Math.Abs(throttle - Throttle) < 0.001f) return;
            Throttle = throttle;
            Log.Info(throttle >= 0.99f
                ? "The shared clock is back to full speed"
                : "The shared clock is held to " + (int)(throttle * 100) + "% of real time: somebody's game cannot simulate any faster");
            ResetSkew();
        }

        public override void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now >= _nextRequestAt)
            {
                _nextRequestAt = now + 1f;
                Net.Send(MessageId.TimeSyncReq, new TimeSyncReqMsg { ClientTicks = DateTime.UtcNow.Ticks, AchievedRate = MeasureAchievedRate(now) }, Channel.State, Delivery.Unreliable);
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
                // The editor's clock stands still, so it is snapped every second to keep the game's UT current
                // for the launch; saying so every second is noise there.
                if (HighLogic.LoadedSceneIsFlight || now >= _nextSnapLogAt)
                {
                    _nextSnapLogAt = now + 10f;
                    Log.Info("UT snapped to server time (drift was " + drift.ToString("F3") + " s" + (rateMismatch ? ", local warp " + localRate + "x vs server " + Rate + "x" : "") + ")");
                }
            }
            else if (HighLogic.LoadedSceneIsFlight)
            {
                // Gentle catch-up at 1x: run a little faster or slower until the drift is inside the dead band.
                var warping = TimeWarp.fetch != null && TimeWarp.CurrentRate != 1f;
                if (!warping && Math.Abs(drift) > SkewDeadBandSeconds)
                {
                    Time.timeScale = BaseScale * Mathf.Clamp(Mathf.Pow(2f, -(float)drift), 0.85f, 1.2f);
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
            OnThrottle(msg.Throttle);
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
