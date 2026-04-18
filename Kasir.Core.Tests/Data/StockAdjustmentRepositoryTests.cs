using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;
using System.Collections.Generic;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class StockAdjustmentRepositoryTests
    {
        private SqliteConnection _db;
        private StockAdjustmentRepository _repo;
        private ProductRepository _productRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new StockAdjustmentRepository(_db);
            _productRepo = new ProductRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void InsertTestProduct(string code, string name)
        {
            _productRepo.Insert(new Product
            {
                ProductCode = code,
                Name = name,
                Price = 1000000,
                Status = "A",
                Unit = "PCS",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });
        }

        private void InsertAdjustmentWithItems(string journalNo, string docDate,
            string docType, string productCode, int qty, int unitPrice)
        {
            var header = new StockAdjustment
            {
                DocType = docType,
                JournalNo = journalNo,
                DocDate = docDate,
                LocationCode = "",
                Remark = "test",
                Control = 1,
                PeriodCode = docDate.Substring(0, 4) + docDate.Substring(5, 2),
                RegisterId = "01",
                ChangedBy = 1
            };
            var items = new List<StockAdjustmentItem>
            {
                new StockAdjustmentItem
                {
                    ProductCode = productCode,
                    Quantity = qty,
                    CostPrice = unitPrice,
                    Value = (long)qty * unitPrice / 100,
                    Reason = "test reason"
                }
            };
            _repo.Insert(header, items);
        }

        private void InsertOpnameRow(string productCode, string productName,
            int qtySystem, int qtyActual, int costPrice, string docDate, string periodCode)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_opname (product_code, product_name, qty_system, qty_actual,
                  cost_price, doc_date, period_code, account_code)
                  VALUES (@code, @name, @qSys, @qAct, @cost, @date, @period, '110.001')",
                SqlHelper.Param("@code", productCode),
                SqlHelper.Param("@name", productName),
                SqlHelper.Param("@qSys", qtySystem),
                SqlHelper.Param("@qAct", qtyActual),
                SqlHelper.Param("@cost", costPrice),
                SqlHelper.Param("@date", docDate),
                SqlHelper.Param("@period", periodCode));
        }

        [Test]
        public void GetAllItemsByDateRange_ReturnsItemsWithHeaderInfo()
        {
            InsertTestProduct("P001", "MINYAK GORENG 2L");
            InsertAdjustmentWithItems("OTM-202604-00001", "2026-04-01", "DAMAGE", "P001", 500, 1500000);

            var items = _repo.GetAllItemsByDateRange("2026-04-01", "2026-04-30");

            items.Count.Should().Be(1);
            items[0].ProductCode.Should().Be("P001");
            items[0].ProductName.Should().Be("MINYAK GORENG 2L");
            items[0].DocDate.Should().Be("2026-04-01");
            items[0].DocType.Should().Be("DAMAGE");
            items[0].Quantity.Should().Be(500);
        }

        [Test]
        public void GetAllItemsByDateRange_EmptyRange_ReturnsEmpty()
        {
            InsertTestProduct("P001", "MINYAK GORENG 2L");
            InsertAdjustmentWithItems("OTM-202604-00001", "2026-04-01", "LOSS", "P001", 100, 500000);

            var items = _repo.GetAllItemsByDateRange("2026-05-01", "2026-05-31");

            items.Count.Should().Be(0);
        }

        [Test]
        public void GetAllItemsByDateRange_FiltersCorrectly()
        {
            InsertTestProduct("P001", "MINYAK GORENG 2L");
            InsertTestProduct("P002", "SABUN CUCI");
            InsertAdjustmentWithItems("OTM-202604-00001", "2026-04-01", "DAMAGE", "P001", 500, 1500000);
            InsertAdjustmentWithItems("OTM-202605-00001", "2026-05-15", "USAGE", "P002", 200, 800000);

            var aprilItems = _repo.GetAllItemsByDateRange("2026-04-01", "2026-04-30");
            aprilItems.Count.Should().Be(1);
            aprilItems[0].ProductCode.Should().Be("P001");

            var mayItems = _repo.GetAllItemsByDateRange("2026-05-01", "2026-05-31");
            mayItems.Count.Should().Be(1);
            mayItems[0].ProductCode.Should().Be("P002");
        }

        [Test]
        public void GetOpnameByDateRange_ReturnsRows()
        {
            InsertOpnameRow("P001", "MINYAK GORENG", 10000, 9500, 1500000, "2026-04-01", "202604");

            var rows = _repo.GetOpnameByDateRange("2026-04-01", "2026-04-30");

            rows.Count.Should().Be(1);
            rows[0].ProductCode.Should().Be("P001");
            rows[0].ProductName.Should().Be("MINYAK GORENG");
            rows[0].QtySystem.Should().Be(10000);
            rows[0].QtyActual.Should().Be(9500);
        }

        [Test]
        public void GetOpnameByDateRange_VarianceCalculation()
        {
            InsertOpnameRow("P001", "MINYAK GORENG", 10000, 9500, 1500000, "2026-04-01", "202604");

            var rows = _repo.GetOpnameByDateRange("2026-04-01", "2026-04-30");

            rows[0].Variance.Should().Be(-500);
            rows[0].VarianceValue.Should().Be(-500L * 1500000);
        }
    }
}
