using System;
using System.Diagnostics;
using KspMp.Shared.Protocol;

namespace KspMp.Server.Services
{
    /// <summary>The single shared timeline. UT advances with the server's wall clock times the warp rate.</summary>
    public sealed class TimeService
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastElapsedSeconds;

        public TimeService(double initialUniversalTime, float rate = 1f)
        {
            UniversalTime = initialUniversalTime;
            Rate = rate;
        }

        public double UniversalTime { get; private set; }
        public float Rate { get; private set; }
        /// <summary>
        /// A brake on the whole timeline, between 0 and 1: the slowest client's achieved rate. A game that
        /// cannot simulate its craft in real time falls behind the shared clock, and gets snapped forward
        /// every few seconds - every vessel jumping along its orbit each time. Holding the universe to the
        /// slowest machine costs everyone some slow motion and keeps them on one timeline.
        /// </summary>
        public float Throttle { get; private set; } = 1f;

        /// <summary>Call once per server tick before reading <see cref="UniversalTime"/>.</summary>
        public void Advance()
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            var dt = elapsed - _lastElapsedSeconds;
            _lastElapsedSeconds = elapsed;
            if (dt > 0) UniversalTime += dt * Rate * Throttle;
        }

        public void SetRate(float rate)
        {
            Advance();
            Rate = rate;
        }

        /// <summary>Returns true when the brake actually moved.</summary>
        public bool SetThrottle(float throttle)
        {
            if (throttle < 0.25f) throttle = 0.25f;
            if (throttle > 1f) throttle = 1f;
            if (Math.Abs(throttle - Throttle) < 0.02f) return false;
            Advance();
            Throttle = throttle;
            return true;
        }

        public void SetUniversalTime(double universalTime)
        {
            Advance();
            UniversalTime = universalTime;
        }

        public TimeSyncMsg Snapshot(long clientTicks)
        {
            Advance();
            return new TimeSyncMsg
            {
                ClientTicks = clientTicks,
                ServerTicks = DateTime.UtcNow.Ticks,
                UniversalTime = UniversalTime,
                Rate = Rate,
                Throttle = Throttle,
            };
        }
    }
}
