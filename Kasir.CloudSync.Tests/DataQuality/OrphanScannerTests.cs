using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.DataQuality;
using Kasir.CloudSync.Tests.TestHelpers;

namespace Kasir.CloudSync.Tests.DataQuality
{
    [TestFixture]
    public class OrphanScannerTests
    {
        [Test]
        public void Scan_Finds_Orphan_Sale_Item_Pointing_At_Missing_Product()
        {
            using var db = TestDb.Create();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO departments (dept_code, name) VALUES ('D1','Dept');
                    INSERT INTO products (product_code, name, dept_code) VALUES ('P1','P1','D1');
                    INSERT INTO sales (doc_type, journal_no, doc_date, sub_code, period_code)
                        VALUES ('SALE','J1','2026-04-25','','202604');
                    -- legitimate sale_item
                    INSERT INTO sale_items (journal_no, product_code, quantity, value)
                        VALUES ('J1','P1',1,100);
                    -- orphan: P-MISSING is not in products
                    INSERT INTO sale_items (journal_no, product_code, quantity, value)
                        VALUES ('J1','P-MISSING',1,200);";
                cmd.ExecuteNonQuery();
            }

            var result = new OrphanScanner(db).Scan();

            result.HasAnyOrphans.Should().BeTrue();
            result.TotalOrphans.Should().Be(1);
            var saleItemsCheck = result.PerCheck.Find(c =>
                c.Check.ChildTable == "sale_items" && c.Check.ChildColumn == "product_code");
            saleItemsCheck.Should().NotBeNull();
            saleItemsCheck.OrphanCount.Should().Be(1);
            saleItemsCheck.SampleKeys.Should().Contain("P-MISSING");
        }

        [Test]
        public void Scan_Clean_Database_Reports_No_Orphans()
        {
            using var db = TestDb.Create();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO departments (dept_code, name) VALUES ('D1','Dept');
                    INSERT INTO products (product_code, name, dept_code) VALUES ('P1','P1','D1');";
                cmd.ExecuteNonQuery();
            }

            var result = new OrphanScanner(db).Scan();

            result.HasAnyOrphans.Should().BeFalse();
            result.TotalOrphans.Should().Be(0);
        }

        [Test]
        public void Scan_Treats_Empty_String_As_Not_An_Orphan()
        {
            // An empty product_code on sale_items normally means a non-product
            // line (service charge etc.) — not an FK orphan.
            using var db = TestDb.Create();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sales (doc_type, journal_no, doc_date, sub_code, period_code)
                        VALUES ('SALE','J1','2026-04-25','','202604');
                    INSERT INTO sale_items (journal_no, product_code, quantity, value)
                        VALUES ('J1','',1,100);";
                cmd.ExecuteNonQuery();
            }

            var result = new OrphanScanner(db).Scan();

            result.HasAnyOrphans.Should().BeFalse();
        }
    }
}
