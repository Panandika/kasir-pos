using NUnit.Framework;
using FluentAssertions;
using Kasir.Models;

namespace Kasir.Tests.Models
{
    [TestFixture]
    public class OpnameReportRowTests
    {
        [Test]
        public void Variance_Shortage_ReturnsNegative()
        {
            var row = new OpnameReportRow { QtySystem = 100, QtyActual = 95 };
            row.Variance.Should().Be(-5);
        }

        [Test]
        public void Variance_Surplus_ReturnsPositive()
        {
            var row = new OpnameReportRow { QtySystem = 100, QtyActual = 105 };
            row.Variance.Should().Be(5);
        }

        [Test]
        public void Variance_Match_ReturnsZero()
        {
            var row = new OpnameReportRow { QtySystem = 100, QtyActual = 100 };
            row.Variance.Should().Be(0);
        }

        // Opname qty is ledger scale (x100): 100 -> 95 is 1 pcs -> 0,95 pcs, a 0,05 pcs
        // shortage. At Rp 15.000 (1500000 x100) that is Rp 750 (75000 x100).
        [Test]
        public void VarianceValue_CalculatesCorrectly()
        {
            var row = new OpnameReportRow
            {
                QtySystem = 100,
                QtyActual = 95,
                CostPrice = 1500000
            };
            row.VarianceValue.Should().Be(-75000L);
        }

        // Review scenario: 72 pcs system, 70 pcs counted, Rp 500 cost -> Rp -1.000, not Rp -100.000.
        [Test]
        public void VarianceValue_DividesOutTheLedgerQtyScale()
        {
            var row = new OpnameReportRow { QtySystem = 7200, QtyActual = 7000, CostPrice = 50000 };
            row.Variance.Should().Be(-200);
            row.VarianceValue.Should().Be(-100000L);
        }

        [Test]
        public void VarianceValue_Surplus_IsPositive_AndRoundsHalfAwayFromZero()
        {
            // +0,5 pcs at Rp 0,01 (1 x100 money): 0.5 money units -> rounds to 1 / -1.
            new OpnameReportRow { QtySystem = 100, QtyActual = 150, CostPrice = 1 }
                .VarianceValue.Should().Be(1L);
            new OpnameReportRow { QtySystem = 150, QtyActual = 100, CostPrice = 1 }
                .VarianceValue.Should().Be(-1L, "a shortage mirrors the surplus, not round-toward-+inf");
            new OpnameReportRow { QtySystem = 1000, QtyActual = 1250, CostPrice = 300000 }
                .VarianceValue.Should().Be(750000L);
        }

        [Test]
        public void VarianceValue_ZeroVariance_ReturnsZero()
        {
            var row = new OpnameReportRow
            {
                QtySystem = 50,
                QtyActual = 50,
                CostPrice = 2000000
            };
            row.VarianceValue.Should().Be(0L);
        }
    }
}
