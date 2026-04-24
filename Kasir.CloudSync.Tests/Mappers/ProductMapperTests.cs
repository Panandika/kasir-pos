using System;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Mappers;
using Kasir.CloudSync.Tests.TestHelpers;

namespace Kasir.CloudSync.Tests.Mappers
{
    [TestFixture]
    public class ProductMapperTests
    {
        private SqliteConnection _db;

        [SetUp] public void SetUp() { _db = TestDb.Create(); }
        [TearDown] public void TearDown() { _db?.Close(); _db?.Dispose(); }

        private void SeedDept() { Exec("INSERT INTO departments (dept_code, name) VALUES ('D1','Dept 1');"); }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Test]
        public void FromReader_Happy_Path_Converts_Types_Correctly()
        {
            SeedDept();
            Exec(@"INSERT INTO products (product_code, name, barcode, dept_code, unit, status,
                                          price, buying_price, qty_min, changed_at, is_consignment)
                   VALUES ('P1','Sampo','8999999','D1','pcs','A',
                           150000, 100000, 10, '2026-04-25 10:30:00','N');");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM products WHERE product_code='P1';";
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();

            var p = ProductMapper.FromReader(reader, out var warnings);

            p.ProductCode.Should().Be("P1");
            p.Name.Should().Be("Sampo");
            p.Barcode.Should().Be("8999999");
            p.DeptCode.Should().Be("D1");
            p.Status.Should().Be("A");
            p.IsConsignment.Should().Be("N");
            p.Price.Should().Be(150000L, "money cents must survive as BIGINT without precision loss");
            p.BuyingPrice.Should().Be(100000L);
            p.QtyMin.Should().Be(10L);
            p.ChangedAt.Should().NotBeNull();
            p.ChangedAt.Value.Year.Should().Be(2026);
            warnings.Should().BeEmpty();
        }

        [Test]
        public void FromReader_Preserves_BigInt_Money_Above_Int32_Max()
        {
            SeedDept();
            // 30M IDR = 3e9 cents; exceeds INT32 max (2.147e9)
            Exec(@"INSERT INTO products (product_code, name, dept_code, price)
                   VALUES ('P-BIG','Expensive','D1', 3000000000);");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM products WHERE product_code='P-BIG';";
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();

            var p = ProductMapper.FromReader(reader, out _);
            p.Price.Should().Be(3_000_000_000L);
        }

        [Test]
        public void FromReader_Null_Columns_Map_To_Null_Or_Zero()
        {
            SeedDept();
            Exec(@"INSERT INTO products (product_code, name, dept_code) VALUES ('P-MIN','Min','D1');");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM products WHERE product_code='P-MIN';";
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();

            var p = ProductMapper.FromReader(reader, out _);
            p.Barcode.Should().BeNull();
            p.VendorCode.Should().BeNull();
            p.ChangedAt.Should().BeNull("changed_at not provided");
            p.Price.Should().Be(0L, "DEFAULT 0 applies");
        }

        [Test]
        public void FromReader_Unparseable_Date_Yields_Null_And_Warning()
        {
            SeedDept();
            // Insert a row then hack changed_at to a bogus value via UPDATE (CHECK allows any TEXT)
            Exec(@"INSERT INTO products (product_code, name, dept_code) VALUES ('P-BAD','Bad','D1');");
            Exec("UPDATE products SET changed_at = 'not-a-date' WHERE product_code = 'P-BAD';");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM products WHERE product_code='P-BAD';";
            using var reader = cmd.ExecuteReader();
            reader.Read().Should().BeTrue();

            var p = ProductMapper.FromReader(reader, out var warnings);
            p.ChangedAt.Should().BeNull();
            warnings.Should().ContainSingle().Which.Should().Contain("P-BAD");
        }

        [Test]
        public void DateParser_Accepts_Multiple_Iso_Shapes()
        {
            DateParser.TryParseIso("2026-04-25 10:30:00", out _).Should().NotBeNull();
            DateParser.TryParseIso("2026-04-25T10:30:00Z", out _).Should().NotBeNull();
            DateParser.TryParseIso("2026-04-25", out _).Should().NotBeNull();
            DateParser.TryParseIso("20260425", out _).Should().NotBeNull();

            DateParser.TryParseIso("", out var w1).Should().BeNull();
            w1.Should().BeFalse("empty input is not a parse failure");

            DateParser.TryParseIso("01/26/2026", out var w2).Should().BeNull();
            w2.Should().BeTrue("US format is not in allowlist -> warning");
        }
    }
}
