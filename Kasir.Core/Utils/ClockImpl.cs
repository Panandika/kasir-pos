using System;

namespace Kasir.Utils
{
    public class ClockImpl : IClock
    {
        public DateTime Now
        {
            get { return DateTime.Now; }
        }

        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }
}
