using System;
using Kasir.Utils;

namespace Kasir.Tests.TestHelpers.Fakes
{
    public class FakeClock : IClock
    {
        private DateTime _now;

        public FakeClock(DateTime fixedTime)
        {
            _now = fixedTime;
        }

        public DateTime Now
        {
            get { return _now; }
        }

        public DateTime UtcNow
        {
            get { return _now.ToUniversalTime(); }
        }

        public void Advance(TimeSpan duration)
        {
            _now = _now.Add(duration);
        }
    }
}
