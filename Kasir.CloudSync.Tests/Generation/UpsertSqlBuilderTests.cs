using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Sinks;

namespace Kasir.CloudSync.Tests.Generation
{
    [TestFixture]
    public class UpsertSqlBuilderTests
    {
        [Test]
        public void Generator_Matches_Hand_Written_PostgresSink_For_Products()
        {
            var generic = UpsertSqlBuilder.Build(TableMappings.Products, batchSize: 1);
            var handWritten = PostgresSink.BuildUpsertSql(1);

            // Both should target products + use product_code in ON CONFLICT.
            generic.Should().StartWith("INSERT INTO products (");
            handWritten.Should().StartWith("INSERT INTO products (");
            generic.Should().Contain("ON CONFLICT (product_code) DO UPDATE SET");
            handWritten.Should().Contain("ON CONFLICT (product_code) DO UPDATE SET");

            // Both must include the same critical columns.
            foreach (var col in new[] { "product_code", "name", "price", "buying_price", "changed_at" })
            {
                generic.Should().Contain(col);
                handWritten.Should().Contain(col);
            }

            // Neither should set the PK in the UPDATE clause (illegal upsert pattern).
            generic.Should().NotContain("product_code = EXCLUDED.product_code");
            handWritten.Should().NotContain("product_code = EXCLUDED.product_code");
        }

        [Test]
        public void Generator_Handles_Composite_PK_For_Sales()
        {
            var sql = UpsertSqlBuilder.Build(TableMappings.Sales, 1);
            sql.Should().Contain("ON CONFLICT (journal_no) DO UPDATE SET");
            sql.Should().NotContain("journal_no = EXCLUDED.journal_no");
        }

        [Test]
        public void Generator_Departments_Has_All_Four_Columns()
        {
            var sql = UpsertSqlBuilder.Build(TableMappings.Departments, 1);
            sql.Should().Contain("dept_code");
            sql.Should().Contain("name");
            sql.Should().Contain("changed_by");
            sql.Should().Contain("changed_at");
            sql.Should().Contain("ON CONFLICT (dept_code)");
        }

        [Test]
        public void Generator_Scales_Per_BatchSize()
        {
            var sql = UpsertSqlBuilder.Build(TableMappings.Departments, 3);
            sql.Should().Contain("@dept_code_0");
            sql.Should().Contain("@dept_code_1");
            sql.Should().Contain("@dept_code_2");
            sql.Should().NotContain("@dept_code_3");
        }

        [Test]
        public void Registry_Lookup_By_Name_Returns_Mapping()
        {
            TableMappings.Get("products").Should().BeSameAs(TableMappings.Products);
            TableMappings.Get("departments").Should().BeSameAs(TableMappings.Departments);
            TableMappings.Get("subsidiaries").Should().BeSameAs(TableMappings.Subsidiaries);
            TableMappings.Get("sales").Should().BeSameAs(TableMappings.Sales);
            TableMappings.Get("sale_items").Should().BeSameAs(TableMappings.SaleItems);
            TableMappings.Get("stock_movements").Should().BeSameAs(TableMappings.StockMovements);
            TableMappings.Get("nonexistent").Should().BeNull();
        }
    }
}
