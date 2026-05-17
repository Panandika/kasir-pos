using System.IO;
using System.Linq;
using FluentAssertions;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Loader;
using Kasir.CloudSync.Snapshot;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Snapshot
{
    // Unit tests for the testable bits of SnapshotBuilder. The full BuildAsync
    // path requires a live Postgres + Kasir.Core embedded Schema.sql; that is
    // exercised by US-P5-1 integration tests.
    //
    // PRD story: US-P2-2
    [TestFixture]
    public class SnapshotBuilderTests
    {
        [Test]
        public void OrderedTableNames_starts_with_LoadOrder_entries()
        {
            var ordered = SnapshotBuilder.OrderedTableNames().ToList();
            var loadOrder = InitialLoader.LoadOrder;
            // First N entries should match LoadOrder in order
            for (int i = 0; i < loadOrder.Count; i++)
            {
                ordered[i].Should().Be(loadOrder[i]);
            }
        }

        [Test]
        public void OrderedTableNames_includes_shifts_at_the_end()
        {
            var ordered = SnapshotBuilder.OrderedTableNames().ToList();
            ordered.Should().Contain("shifts");
            ordered.IndexOf("shifts").Should().Be(ordered.Count - 1);
        }

        [Test]
        public void OrderedTableNames_covers_all_TableMappings()
        {
            var ordered = SnapshotBuilder.OrderedTableNames().ToList();
            foreach (var key in TableMappings.All.Keys)
            {
                ordered.Should().Contain(key);
            }
        }

        [Test]
        public void OrderedTableNames_has_no_duplicates()
        {
            var ordered = SnapshotBuilder.OrderedTableNames().ToList();
            ordered.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void BuildInsertSql_produces_parameterised_insert()
        {
            var mapping = TableMappings.Get("departments");
            mapping.Should().NotBeNull();
            var cols = mapping.Columns.Select(c => c.Name).ToList();
            var sql = SnapshotBuilder.BuildInsertSql(mapping, cols);

            sql.Should().StartWith("INSERT OR REPLACE INTO [departments] (");
            foreach (var c in cols)
            {
                sql.Should().Contain("[" + c + "]");
                sql.Should().Contain("@" + c);
            }
        }

        [Test]
        public void ComputeSha256_is_deterministic_for_same_content()
        {
            var path = Path.Combine(Path.GetTempPath(),
                "snap-" + System.Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
                var a = SnapshotBuilder.ComputeSha256(path);
                var b = SnapshotBuilder.ComputeSha256(path);
                a.Should().Be(b);
                a.Length.Should().Be(64);
                a.Should().MatchRegex("^[0-9a-f]{64}$");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void ComputeSha256_differs_for_different_content()
        {
            var p1 = Path.Combine(Path.GetTempPath(),
                "snap-" + System.Guid.NewGuid().ToString("N") + ".bin");
            var p2 = Path.Combine(Path.GetTempPath(),
                "snap-" + System.Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(p1, new byte[] { 1, 2, 3 });
                File.WriteAllBytes(p2, new byte[] { 4, 5, 6 });
                SnapshotBuilder.ComputeSha256(p1).Should().NotBe(SnapshotBuilder.ComputeSha256(p2));
            }
            finally
            {
                if (File.Exists(p1)) File.Delete(p1);
                if (File.Exists(p2)) File.Delete(p2);
            }
        }

        [Test]
        public void SupportedSchemaVersion_is_1()
        {
            SnapshotBuilder.SupportedSchemaVersion.Should().Be(1);
        }
    }
}
