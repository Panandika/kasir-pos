using System.Collections.Generic;
using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Models;
using Kasir.CloudSync.Sinks;

namespace Kasir.CloudSync.Tests.Sinks
{
    [TestFixture]
    public class PostgresSinkTests
    {
        [Test]
        public void BuildUpsertSql_Generates_Correct_Shape_For_Single_Row()
        {
            var sql = PostgresSink.BuildUpsertSql(1);

            sql.Should().StartWith("INSERT INTO products (");
            sql.Should().Contain("product_code");
            sql.Should().Contain("price");
            sql.Should().Contain("buying_price");
            sql.Should().Contain("VALUES (@product_code_0");
            sql.Should().Contain("ON CONFLICT (product_code) DO UPDATE SET");
            sql.Should().Contain("name = EXCLUDED.name");
            sql.Should().NotContain("product_code = EXCLUDED.product_code",
                "the PK itself must not be updated");
            sql.Should().EndWith(";");
        }

        [Test]
        public void BuildUpsertSql_Set_List_Has_No_Leading_Separator()
        {
            var sql = PostgresSink.BuildUpsertSql(1);

            sql.Should().Contain("DO UPDATE SET name = EXCLUDED.name",
                "the first assignment follows SET directly");
            sql.Should().NotContain("SET ,", "a leading comma is a Postgres syntax error (42601)");
            sql.Should().NotContain(", ,");
        }

        [Test]
        public void DedupeByProductCode_Keeps_Last_Row_Per_Key_In_First_Seen_Order()
        {
            var rows = new List<Product>
            {
                new Product { ProductCode = "A", Name = "A-old" },
                new Product { ProductCode = "B", Name = "B" },
                new Product { ProductCode = "A", Name = "A-new" },
            };

            var result = new List<Product>(PostgresSink.DedupeByProductCode(rows));

            result.Should().HaveCount(2, "one statement may not upsert the same key twice (21000)");
            result[0].ProductCode.Should().Be("A");
            result[0].Name.Should().Be("A-new", "the newest queue entry wins");
            result[1].ProductCode.Should().Be("B");
        }

        [Test]
        public void DedupeByProductCode_Returns_Input_When_Keys_Are_Unique()
        {
            var rows = new List<Product>
            {
                new Product { ProductCode = "A" },
                new Product { ProductCode = "B" },
            };

            PostgresSink.DedupeByProductCode(rows).Should().BeSameAs(rows);
        }

        [Test]
        public void BuildUpsertSql_Scales_Parameter_Names_Per_Row()
        {
            var sql = PostgresSink.BuildUpsertSql(3);

            sql.Should().Contain("@product_code_0");
            sql.Should().Contain("@product_code_1");
            sql.Should().Contain("@product_code_2");
            sql.Should().NotContain("@product_code_3");
        }

        [Test]
        public void BuildUpsertSql_Is_Idempotent_For_Same_BatchSize()
        {
            var a = PostgresSink.BuildUpsertSql(5);
            var b = PostgresSink.BuildUpsertSql(5);
            a.Should().Be(b, "SQL generation must be deterministic for caching");
        }
    }
}
