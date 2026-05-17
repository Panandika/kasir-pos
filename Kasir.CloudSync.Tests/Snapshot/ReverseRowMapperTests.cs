using System;
using System.Collections.Generic;
using System.Data;
using FluentAssertions;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Mappers;
using Kasir.CloudSync.Snapshot;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Snapshot
{
    // Tests the inverse mapper used by SnapshotBuilder. Drives the per-kind
    // conversion via a DataTableReader (works without a real Postgres).
    //
    // PRD story: US-P2-1
    [TestFixture]
    public class ReverseRowMapperTests
    {
        private static TableMapping Mapping(params (string Name, ColumnKind Kind)[] cols)
        {
            var list = new List<ColumnMapping>();
            foreach (var (n, k) in cols)
            {
                list.Add(new ColumnMapping(n, k));
            }
            return new TableMapping("test", list);
        }

        private static DataTableReader MakeReader(
            (string Col, Type Type, object Value)[] rows)
        {
            var dt = new DataTable();
            foreach (var r in rows)
            {
                dt.Columns.Add(r.Col, r.Type);
            }
            var row = dt.NewRow();
            foreach (var r in rows)
            {
                row[r.Col] = r.Value ?? DBNull.Value;
            }
            dt.Rows.Add(row);
            var reader = dt.CreateDataReader();
            reader.Read();
            return reader;
        }

        [Test]
        public void Text_passes_through()
        {
            using var reader = MakeReader(new[]
            {
                ("name", typeof(string), (object)"Indomilk"),
            });
            var mapping = Mapping(("name", ColumnKind.Text));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["name"].Should().Be("Indomilk");
        }

        [Test]
        public void Text_preserves_empty_string()
        {
            using var reader = MakeReader(new[] { ("name", typeof(string), (object)"") });
            var mapping = Mapping(("name", ColumnKind.Text));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["name"].Should().Be(string.Empty);
        }

        [Test]
        public void Text_NULL_becomes_null()
        {
            using var reader = MakeReader(new[] { ("name", typeof(string), (object)null) });
            var mapping = Mapping(("name", ColumnKind.Text));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["name"].Should().BeNull();
        }

        [Test]
        public void BigintMoney_returns_long_no_scaling()
        {
            using var reader = MakeReader(new[] { ("price_cents", typeof(long), (object)1_500_000L) });
            var mapping = Mapping(("price_cents", ColumnKind.BigintMoney));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["price_cents"].Should().Be(1_500_000L);
            result["price_cents"].Should().BeOfType<long>();
        }

        [Test]
        public void BigintQty_returns_long()
        {
            using var reader = MakeReader(new[] { ("qty", typeof(long), (object)42L) });
            var mapping = Mapping(("qty", ColumnKind.BigintQty));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["qty"].Should().Be(42L);
        }

        [Test]
        public void Int_returns_int_when_possible()
        {
            using var reader = MakeReader(new[] { ("dept_id", typeof(int), (object)7) });
            var mapping = Mapping(("dept_id", ColumnKind.Int));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["dept_id"].Should().Be(7);
            result["dept_id"].Should().BeOfType<int>();
        }

        [Test]
        public void Int_falls_back_to_long_when_widened()
        {
            using var reader = MakeReader(new[] { ("widened", typeof(long), (object)3_500_000_000L) });
            var mapping = Mapping(("widened", ColumnKind.Int));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["widened"].Should().Be(3_500_000_000L);
        }

        [Test]
        public void TimestampTz_DateTime_utc_formats_as_iso()
        {
            var ts = new DateTime(2026, 5, 17, 14, 30, 0, DateTimeKind.Utc);
            using var reader = MakeReader(new[] { ("ts", typeof(DateTime), (object)ts) });
            var mapping = Mapping(("ts", ColumnKind.TimestampTz));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["ts"].Should().Be("2026-05-17T14:30:00.000+00:00");
        }

        [Test]
        public void TimestampTz_DateTimeOffset_with_local_offset_normalized_to_utc()
        {
            var ts = new DateTimeOffset(2026, 5, 17, 21, 30, 0, TimeSpan.FromHours(7));
            using var reader = MakeReader(new[] { ("ts", typeof(DateTimeOffset), (object)ts) });
            var mapping = Mapping(("ts", ColumnKind.TimestampTz));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            // 21:30 +07:00 == 14:30 UTC
            result["ts"].Should().Be("2026-05-17T14:30:00.000+00:00");
        }

        [Test]
        public void TimestampTz_Unspecified_kind_treated_as_utc()
        {
            var ts = new DateTime(2026, 5, 17, 14, 30, 0, DateTimeKind.Unspecified);
            using var reader = MakeReader(new[] { ("ts", typeof(DateTime), (object)ts) });
            var mapping = Mapping(("ts", ColumnKind.TimestampTz));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["ts"].Should().Be("2026-05-17T14:30:00.000+00:00");
        }

        [Test]
        public void TimestampTz_NULL_becomes_null()
        {
            using var reader = MakeReader(new[] { ("ts", typeof(DateTime), (object)null) });
            var mapping = Mapping(("ts", ColumnKind.TimestampTz));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["ts"].Should().BeNull();
        }

        [Test]
        public void Missing_column_in_reader_becomes_null()
        {
            using var reader = MakeReader(new[] { ("present", typeof(string), (object)"x") });
            var mapping = Mapping(
                ("present", ColumnKind.Text),
                ("missing", ColumnKind.Text));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["missing"].Should().BeNull();
        }

        [Test]
        public void Multiple_columns_in_one_row()
        {
            using var reader = MakeReader(new[]
            {
                ("kode", typeof(string), (object)"P001"),
                ("price", typeof(long), (object)1500L),
                ("qty", typeof(int), (object)10),
                ("created_at", typeof(DateTime), (object)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            });
            var mapping = Mapping(
                ("kode", ColumnKind.Text),
                ("price", ColumnKind.BigintMoney),
                ("qty", ColumnKind.Int),
                ("created_at", ColumnKind.TimestampTz));
            var result = ReverseRowMapper.FromReaderCore(mapping, reader);
            result["kode"].Should().Be("P001");
            result["price"].Should().Be(1500L);
            result["qty"].Should().Be(10);
            result["created_at"].Should().Be("2026-01-01T00:00:00.000+00:00");
        }

        [Test]
        public void Null_mapping_throws()
        {
            using var reader = MakeReader(new[] { ("c", typeof(string), (object)"v") });
            Action act = () => ReverseRowMapper.FromReaderCore(null, reader);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Null_reader_throws()
        {
            var mapping = Mapping(("c", ColumnKind.Text));
            Action act = () => ReverseRowMapper.FromReaderCore(mapping, null);
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
