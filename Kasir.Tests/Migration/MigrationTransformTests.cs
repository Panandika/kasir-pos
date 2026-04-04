using System;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Migration;

namespace Kasir.Tests.Migration
{
    [TestFixture]
    public class MigrationTransformTests
    {
        // --- MoneyToInteger ---

        [TestCase(150.0, 15000L)]
        [TestCase(0.0, 0L)]
        [TestCase(1500.50, 150050L)]
        [TestCase(99999.99, 9999999L)]
        [TestCase(-50.0, -5000L)]
        public void MoneyToInteger_ConvertsCorrectly(double input, long expected)
        {
            MigrationTransforms.MoneyToInteger(input).Should().Be(expected);
        }

        [Test]
        public void MoneyToInteger_NullReturnsZero()
        {
            MigrationTransforms.MoneyToInteger(null).Should().Be(0);
        }

        [Test]
        public void MoneyToInteger_DbNullReturnsZero()
        {
            MigrationTransforms.MoneyToInteger(DBNull.Value).Should().Be(0);
        }

        [Test]
        public void MoneyToInteger_StringValue()
        {
            MigrationTransforms.MoneyToInteger("1500.50").Should().Be(150050);
        }

        [Test]
        public void MoneyToInteger_DecimalValue()
        {
            MigrationTransforms.MoneyToInteger(150.0m).Should().Be(15000);
        }

        // --- PercentToInteger ---

        [TestCase(5.5, 550)]
        [TestCase(10.0, 1000)]
        [TestCase(0.0, 0)]
        [TestCase(2.25, 225)]
        public void PercentToInteger_ConvertsCorrectly(double input, int expected)
        {
            MigrationTransforms.PercentToInteger(input).Should().Be(expected);
        }

        // --- DateToIso ---

        [Test]
        public void DateToIso_DateTime()
        {
            MigrationTransforms.DateToIso(new DateTime(2026, 4, 4)).Should().Be("2026-04-04");
        }

        [Test]
        public void DateToIso_IsoString()
        {
            MigrationTransforms.DateToIso("2026-04-04").Should().Be("2026-04-04");
        }

        [Test]
        public void DateToIso_CompactString()
        {
            MigrationTransforms.DateToIso("20260404").Should().Be("2026-04-04");
        }

        [Test]
        public void DateToIso_NullReturnsEmpty()
        {
            MigrationTransforms.DateToIso(null).Should().Be("");
        }

        [Test]
        public void DateToIso_EmptyReturnsEmpty()
        {
            MigrationTransforms.DateToIso("").Should().Be("");
        }

        [Test]
        public void DateToIso_DbNullReturnsEmpty()
        {
            MigrationTransforms.DateToIso(DBNull.Value).Should().Be("");
        }

        // --- TrimDosString ---

        [Test]
        public void TrimDosString_TrimsTrailingSpaces()
        {
            MigrationTransforms.TrimDosString("HELLO   ").Should().Be("HELLO");
        }

        [Test]
        public void TrimDosString_NullReturnsEmpty()
        {
            MigrationTransforms.TrimDosString(null).Should().Be("");
        }

        [Test]
        public void TrimDosString_DbNullReturnsEmpty()
        {
            MigrationTransforms.TrimDosString(DBNull.Value).Should().Be("");
        }

        // --- PeriodFromFilename ---

        [TestCase("acc_0126.dbf", "202601")]
        [TestCase("acc_1225.dbf", "202512")]
        [TestCase("acc_0199.dbf", "199901")]
        [TestCase("inv_0326.dbf", "202603")]
        public void PeriodFromFilename_ConvertsCorrectly(string filename, string expected)
        {
            MigrationTransforms.PeriodFromFilename(filename).Should().Be(expected);
        }

        [Test]
        public void PeriodFromFilename_InvalidReturnsEmpty()
        {
            MigrationTransforms.PeriodFromFilename("invalid.dbf").Should().Be("");
        }

        [Test]
        public void PeriodFromFilename_NullReturnsEmpty()
        {
            MigrationTransforms.PeriodFromFilename(null).Should().Be("");
        }

        // --- MapStatusCode ---

        [TestCase("A", "A")]
        [TestCase("I", "I")]
        [TestCase("D", "I")]
        [TestCase("T", "Y")]
        [TestCase("F", "N")]
        public void MapStatusCode_MapsCorrectly(string input, string expected)
        {
            MigrationTransforms.MapStatusCode(input, "A").Should().Be(expected);
        }

        [Test]
        public void MapStatusCode_NullReturnsDefault()
        {
            MigrationTransforms.MapStatusCode(null, "A").Should().Be("A");
        }

        // --- MapControl ---

        [TestCase("N", 1)]
        [TestCase("P", 2)]
        [TestCase("D", 3)]
        [TestCase("E", 4)]
        [TestCase("R", 5)]
        [TestCase("", 1)]
        [TestCase("2", 2)]
        public void MapControl_MapsCorrectly(string input, int expected)
        {
            MigrationTransforms.MapControl(input).Should().Be(expected);
        }

        // --- MigrationResult ---

        [Test]
        public void MigrationResult_AggregatesTableResults()
        {
            var result = new MigrationResult();
            result.AddTableResult(new TableMigrationResult
            {
                TableName = "products",
                SourceCount = 24457,
                MigratedCount = 24457,
                ElapsedSeconds = 5.0
            });
            result.AddTableResult(new TableMigrationResult
            {
                TableName = "vendors",
                SourceCount = 754,
                MigratedCount = 750,
                ErrorCount = 4,
                ElapsedSeconds = 1.0
            });

            result.TotalSourceRows.Should().Be(25211);
            result.TotalMigrated.Should().Be(25207);
            result.TotalErrors.Should().Be(4);
            result.Success.Should().BeFalse();
        }

        [Test]
        public void MigrationResult_NoErrors_IsSuccess()
        {
            var result = new MigrationResult();
            result.AddTableResult(new TableMigrationResult
            {
                TableName = "products",
                SourceCount = 100,
                MigratedCount = 100
            });

            result.Success.Should().BeTrue();
        }

        // --- FieldMappings ---

        [Test]
        public void FieldMappings_MigrationOrder_HasExpectedTables()
        {
            FieldMappings.MigrationOrder.Should().Contain("products");
            FieldMappings.MigrationOrder.Should().Contain("roles");
            FieldMappings.MigrationOrder.Should().Contain("payables_register");
        }

        [Test]
        public void FieldMappings_RolesBeforeUsers()
        {
            var order = FieldMappings.MigrationOrder;
            int rolesIdx = Array.IndexOf(order, "roles");
            int usersIdx = Array.IndexOf(order, "users");
            rolesIdx.Should().BeLessThan(usersIdx);
        }

        [Test]
        public void FieldMappings_ProductsBeforeSaleItems()
        {
            var order = FieldMappings.MigrationOrder;
            int productsIdx = Array.IndexOf(order, "products");
            int saleItemsIdx = Array.IndexOf(order, "sale_items");
            productsIdx.Should().BeLessThan(saleItemsIdx);
        }

        [Test]
        public void ExpectedCounts_MatchKnownValues()
        {
            FieldMappings.ExpectedCounts.Products.Should().Be(24457);
            FieldMappings.ExpectedCounts.Vendors.Should().Be(754);
            FieldMappings.ExpectedCounts.Departments.Should().Be(194);
            FieldMappings.ExpectedCounts.Payables.Should().Be(80767);
        }
    }
}
