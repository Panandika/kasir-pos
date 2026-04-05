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

        [Test]
        public void VarianceValue_CalculatesCorrectly()
        {
            var row = new OpnameReportRow
            {
                QtySystem = 100,
                QtyActual = 95,
                CostPrice = 1500000
            };
            // Variance = -5, CostPrice = 1500000 → VarianceValue = -7500000
            row.VarianceValue.Should().Be(-7500000L);
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
