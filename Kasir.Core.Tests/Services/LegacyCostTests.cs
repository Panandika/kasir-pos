using FluentAssertions;
using Kasir.Services;
using NUnit.Framework;

namespace Kasir.Tests.Services
{
    // D28: same cases as the dashboard's legacy_line_unit_cost (migration 0071).
    [TestFixture]
    public class LegacyCostTests
    {
        [TestCase(900000, 1000000, 100000, 900000, TestName = "cogs wins when > 0")]
        [TestCase(800000, 1000000, 0, 800000, TestName = "cogs wins over a pre-discount unit_price")]
        [TestCase(0, 1000000, 100000, 900000, TestName = "cogs 0 falls back to unit_price - disc_value")]
        [TestCase(0, 1000000, 0, 1000000, TestName = "cogs 0, no discount -> unit_price")]
        [TestCase(0, 100000, 250000, 0, TestName = "fallback never goes below 0")]
        [TestCase(-5, 1000000, 100000, 900000, TestName = "negative cogs is treated as missing")]
        public void UnitCost_FollowsTheD28Rule(long cogs, long unitPrice, long discValue, long expected)
        {
            LegacyCost.UnitCost(cogs, unitPrice, discValue).Should().Be(expected);
        }
    }
}
