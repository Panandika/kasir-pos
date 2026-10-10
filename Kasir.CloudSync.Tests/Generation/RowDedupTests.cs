using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Kasir.CloudSync.Generation;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Generation
{
    [TestFixture]
    public class RowDedupTests
    {
        private static IDictionary<string, object> Row(params (string k, object v)[] cols) =>
            cols.ToDictionary(c => c.k, c => c.v);

        private static readonly TableMapping Composite = new TableMapping("t", new[]
        {
            new ColumnMapping("a", ColumnKind.Text, isPrimaryKey: true),
            new ColumnMapping("b", ColumnKind.Text, isPrimaryKey: true),
            new ColumnMapping("v", ColumnKind.Int),
        });

        [Test]
        public void SameKeyTwice_KeepsTheLastRow_InFirstSeenOrder()
        {
            var rows = new List<IDictionary<string, object>>
            {
                Row(("journal_no", "S1"), ("control", 1)),
                Row(("journal_no", "S2"), ("control", 1)),
                Row(("journal_no", "S1"), ("control", 3)),
            };

            var result = RowDedup.ByPrimaryKey(TableMappings.Sales, rows).ToList();

            result.Select(r => r["journal_no"]).Should().Equal("S1", "S2");
            result[0]["control"].Should().Be(3, "the newest queue entry wins");
        }

        [Test]
        public void NoDuplicates_ReturnsTheInputInstance()
        {
            var rows = new List<IDictionary<string, object>> { Row(("journal_no", "S1")), Row(("journal_no", "S2")) };

            RowDedup.ByPrimaryKey(TableMappings.Sales, rows).Should().BeSameAs(rows);
        }

        [Test]
        public void CompositeKey_UsesEveryPkColumn_WithoutSeparatorCollisions()
        {
            var rows = new List<IDictionary<string, object>>
            {
                Row(("a", "x|y"), ("b", "z"), ("v", 1)),
                Row(("a", "x"), ("b", "y|z"), ("v", 2)),
                Row(("a", "x"), ("b", "q"), ("v", 3)),
                Row(("a", "x"), ("b", "q"), ("v", 4)),
            };

            var result = RowDedup.ByPrimaryKey(Composite, rows).ToList();

            result.Select(r => r["v"]).Should().Equal(1, 2, 4);
        }
    }
}
