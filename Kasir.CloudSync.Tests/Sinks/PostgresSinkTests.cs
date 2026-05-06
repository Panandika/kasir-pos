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
