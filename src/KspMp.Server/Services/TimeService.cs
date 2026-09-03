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

        /// <summary>Call once per server tick before reading <see cref="UniversalTime"/>.</summary>
        public void Advance()
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            var dt = elapsed - _lastElapsedSeconds;
            _lastElapsedSeconds = elapsed;
            if (dt > 0) UniversalTime += dt * Rate;
        }

        public void SetRate(float rate)
        {
            Advance();
            Rate = rate;
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
            };
        }
    }
}
