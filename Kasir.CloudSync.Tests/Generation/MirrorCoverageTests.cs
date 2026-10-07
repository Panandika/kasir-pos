using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Snapshot;
using Kasir.CloudSync.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Generation
{
    // Guards the Supabase mirror coverage: which POS tables/columns TableMappings
    // ships, which SkipList excludes, and which tables a register snapshot restores.
    // Gap analysis + rationale: PR "feat: mirror purchase_items and shifts".
    [TestFixture]
    public class MirrorCoverageTests
    {
        private static Dictionary<string, HashSet<string>> PosColumns()
        {
            var result = new Dictionary<string, HashSet<string>>();
            using var db = TestDb.Create();
            var tables = new List<string>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) tables.Add(rd.GetString(0));
            }
            foreach (var t in tables)
            {
                var cols = new HashSet<string>();
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info([{t}]);";
                using var rd = cmd.ExecuteReader();
                while (rd.Read()) cols.Add(rd.GetString(1));
                result[t] = cols;
            }
            return result;
        }

        [Test]
        public void Purchase_items_is_mirrored_with_the_columns_the_POS_writes()
        {
            var m = TableMappings.Get("purchase_items");
            m.Should().NotBeNull("purchasing reports need invoice lines in the cloud mirror");
            var cols = m.Columns.Select(c => c.Name).ToList();
            // Exactly what PurchaseRepository inserts, plus the legacy cost columns.
            cols.Should().Contain(new[]
            {
                "id", "journal_no", "order_ref", "product_code", "remark", "quantity",
                "value", "unit_price", "disc_pct", "disc_value", "qty_order", "cogs"
            });
            m.PrimaryKeyColumns.Should().Equal("id");
        }

        [Test]
        public void No_table_is_both_mapped_and_skipped()
        {
            TableMappings.All.Keys.Intersect(SkipList.Excluded.Keys)
                .Should().BeEmpty("a table is either mirrored or deliberately excluded, never both");
        }

        [Test]
        public void Every_mapped_column_exists_in_the_POS_schema()
        {
            var pos = PosColumns();
            foreach (var kv in TableMappings.All)
            {
                pos.Should().ContainKey(kv.Key);
                foreach (var c in kv.Value.Columns)
                    pos[kv.Key].Should().Contain(c.Name, $"{kv.Key}.{c.Name} is mapped");
            }
        }

        [Test]
        public void Every_POS_column_of_a_mapped_table_is_mapped_or_explicitly_excluded()
        {
            var pos = PosColumns();
            var unaccounted = new List<string>();
            foreach (var kv in TableMappings.All)
            {
                var mapped = new HashSet<string>(kv.Value.Columns.Select(c => c.Name));
                foreach (var col in pos[kv.Key])
                {
                    if (mapped.Contains(col)) continue;
                    if (SkipList.ExcludedColumns.ContainsKey(kv.Key + "." + col)) continue;
                    unaccounted.Add(kv.Key + "." + col);
                }
            }
            unaccounted.Should().BeEmpty("new POS columns must be mirrored or excluded with a reason");
        }

        [Test]
        public void Excluded_columns_are_not_also_mapped()
        {
            foreach (var key in SkipList.ExcludedColumns.Keys)
            {
                var parts = key.Split('.');
                var m = TableMappings.Get(parts[0]);
                m.Should().NotBeNull($"{key} names a mapped table");
                m.Columns.Select(c => c.Name).Should().NotContain(parts[1]);
            }
        }

        // Kinds whose reverse read (ReverseRowMapper) works for a given Postgres type.
        // Int also accepts BIGINT: MapValue widens to long.
        private static readonly Dictionary<ColumnKind, string[]> CompatiblePgTypes = new()
        {
            { ColumnKind.Text, new[] { "TEXT" } },
            { ColumnKind.Int, new[] { "INTEGER", "BIGINT", "BIGSERIAL" } },
            { ColumnKind.BigintMoney, new[] { "BIGINT", "BIGSERIAL" } },
            { ColumnKind.BigintQty, new[] { "BIGINT", "BIGSERIAL" } },
            { ColumnKind.TimestampTz, new[] { "TIMESTAMPTZ" } },
        };

        [Test]
        public void Every_mapped_column_kind_matches_its_cloud_DDL_type()
        {
            // A Text mapping over an INTEGER column makes SnapshotBuilder throw
            // InvalidCastException (GetString on int4) and abort the whole build.
            var sqlDir = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Kasir.CloudSync", "Sql");
            var mismatches = new List<string>();
            foreach (var kv in TableMappings.All)
            {
                var ddl = File.ReadAllText(Path.Combine(sqlDir, kv.Key + ".sql"));
                var block = Regex.Match(ddl,
                    @"CREATE TABLE (?:IF NOT EXISTS )?" + kv.Key + @"\s*\((.*?)\n\);",
                    RegexOptions.Singleline);
                block.Success.Should().BeTrue($"Sql/{kv.Key}.sql has a CREATE TABLE block");
                var types = new Dictionary<string, string>();
                foreach (var line in block.Groups[1].Value.Split('\n'))
                {
                    var m = Regex.Match(line, @"^\s*([a-z_0-9]+)\s+([A-Za-z]+)");
                    if (m.Success) types[m.Groups[1].Value] = m.Groups[2].Value.ToUpperInvariant();
                }
                foreach (var c in kv.Value.Columns)
                {
                    types.Should().ContainKey(c.Name, $"Sql/{kv.Key}.sql declares {c.Name}");
                    if (!CompatiblePgTypes[c.Kind].Contains(types[c.Name]))
                        mismatches.Add($"{kv.Key}.{c.Name}: {c.Kind} vs {types[c.Name]}");
                }
            }
            mismatches.Should().BeEmpty();
        }

        [Test]
        public void Shifts_cloud_key_includes_register_id()
        {
            // shifts.id is a per-register rowid: KLR-01 and KLR-02 both have shift 1.
            TableMappings.Shifts.PrimaryKeyColumns.Should().BeEquivalentTo(new[] { "id", "register_id" });
        }

        [Test]
        public void Snapshot_does_not_restore_shifts()
        {
            // Restoring another register's (or this slot's old PC's) open shift would
            // drop a freshly paired register into a shift it never opened.
            SnapshotBuilder.OrderedTableNames().Should().NotContain("shifts");
        }

        [Test]
        public void Snapshot_restores_purchase_items_after_purchases()
        {
            var ordered = SnapshotBuilder.OrderedTableNames().ToList();
            ordered.Should().Contain("purchase_items");
            ordered.IndexOf("purchase_items").Should().BeGreaterThan(ordered.IndexOf("purchases"));
        }
    }
}
