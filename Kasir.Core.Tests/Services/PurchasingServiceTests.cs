using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class PurchasingServiceTests
    {
        private SqliteConnection _db;
        private PurchasingService _service;
        private FakeClock _clock;
        private StockMovementRepository _movementRepo;
        private PayablesRepository _payablesRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(new System.DateTime(2026, 4, 4, 10, 0, 0));
            _service = new PurchasingService(_db, _clock);
            _movementRepo = new StockMovementRepository(_db);
            _payablesRepo = new PayablesRepository(_db);

            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");
            // Post-cutover mode: the POS cost engine maintains products.cost_price.
            configRepo.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");

            // Seed a product
            var productRepo = new ProductRepository(_db);
            productRepo.Insert(new Product
            {
                ProductCode = "P001", Name = "TEST PRODUCT", Price = 500000,
                BuyingPrice = 300000, Status = "A", OpenPrice = "N",
                VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });

            // Seed a vendor
            var vendorRepo = new SubsidiaryRepository(_db);
            vendorRepo.Insert(new Subsidiary
            {
                SubCode = "V001", Name = "TEST VENDOR", GroupCode = "1", Status = "A"
            });
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        [Test]
        public void CreatePurchaseOrder_GeneratesJournalNo()
        {
            var order = new Order { SubCode = "V001" };
            var items = new List<OrderItem>
            {
                new OrderItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 }
            };

            string jnl = _service.CreatePurchaseOrder(order, items, 1);

            jnl.Should().Contain("OMS");
            var saved = new OrderRepository(_db).GetByJournalNo(jnl);
            saved.Should().NotBeNull();
            saved.TotalValue.Should().Be(3000000);
        }

        [Test]
        public void CreateGoodsReceipt_CreatesStockMovement()
        {
            var receipt = new Purchase { SubCode = "V001" };
            var items = new List<PurchaseItem>
            {
                new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 }
            };

            string jnl = _service.CreateGoodsReceipt(receipt, items, 1);

            jnl.Should().Contain("BPB");

            // Verify stock movement created
            int stock = _movementRepo.GetStockOnHand("P001");
            stock.Should().Be(10);
        }

        [Test]
        public void CreatePurchaseInvoice_CreatesAPEntry()
        {
            var invoice = new Purchase { SubCode = "V001", DueDate = "2026-05-04" };
            var items = new List<PurchaseItem>
            {
                new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 }
            };

            string jnl = _service.CreatePurchaseInvoice(invoice, items, 1);

            jnl.Should().Contain("MSK");

            // Verify AP entry
            var ap = _payablesRepo.GetByJournalNo(jnl);
            ap.Should().NotBeNull();
            ap.Amount.Should().Be(3000000);
            ap.IsPaid.Should().Be("N");
        }

        [Test]
        public void CreatePurchaseReturn_AdjustsStock()
        {
            // First receive 10 units
            _service.CreateGoodsReceipt(
                new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } },
                1);

            // Return 3 units
            var ret = new Purchase { SubCode = "V001" };
            var retItems = new List<PurchaseItem>
            {
                new PurchaseItem { ProductCode = "P001", Quantity = 3, UnitPrice = 300000 }
            };

            string jnl = _service.CreatePurchaseReturn(ret, retItems, false, 1);

            jnl.Should().Contain("RMS");
            _movementRepo.GetStockOnHand("P001").Should().Be(7); // 10 - 3
        }

        // PR-K2: a purchase updates the perpetual average on products.cost_price.
        [Test]
        public void CreatePurchaseInvoice_UpdatesCostPrice_PerpetualAvg()
        {
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } },
                1);
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 400000 } },
                1);

            new ProductRepository(_db).GetByCode("P001").CostPrice.Should().Be(350000);
        }

        [Test]
        public void PurchaseReturn_DoesNotChangeCostPrice()
        {
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } },
                1);

            // Return at a different price than the average: cost_price must not move.
            _service.CreatePurchaseReturn(
                new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 3, UnitPrice = 250000 } },
                false, 1);

            new ProductRepository(_db).GetByCode("P001").CostPrice.Should().Be(300000);
        }

        [Test]
        public void PurchaseReturn_WritesReturnOutMovement()
        {
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } },
                1);

            string jnl = _service.CreatePurchaseReturn(
                new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 3, UnitPrice = 300000 } },
                false, 1);

            var m = _movementRepo.GetByJournal(jnl);
            m.Should().ContainSingle();
            m[0].MovementType.Should().Be("RETURN_OUT");
            m[0].QtyOut.Should().Be(3);
        }

        // Fallback chain step 1: cost_price 0 -> last PURCHASE unit_price by doc_date (via
        // JOIN to purchases; purchase_items has no doc_date). Returns and zero prices skipped.
        [Test]
        public void CalculateAverageCost_FallbackToPurchaseUnitPrice()
        {
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", DocDate = "2026-03-01" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 310000 } },
                1);
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", DocDate = "2026-03-20" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 330000 } },
                1);
            // Entered later but dated earlier: must not win on insertion order.
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", DocDate = "2026-03-10" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 320000 } },
                1);
            // A later purchase return is not a purchase price.
            _service.CreatePurchaseReturn(
                new Purchase { SubCode = "V001", DocDate = "2026-03-30" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 1, UnitPrice = 999999 } },
                false, 1);

            SqlHelper.ExecuteNonQuery(_db, "UPDATE products SET cost_price = 0 WHERE product_code = 'P001'");

            new InventoryService(_db).CalculateAverageCost("P001").Should().Be(330000);
        }
        // Review LOW-4: the fallback skips deleted (control = 3) purchases and breaks
        // same-date ties deterministically by the later-entered purchase.
        [Test]
        public void CalculateAverageCost_Fallback_SkipsDeletedPurchase()
        {
            _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", DocDate = "2026-03-01" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 310000 } },
                1);
            string deleted = _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", DocDate = "2026-03-20" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 999000 } },
                1);
            SqlHelper.ExecuteNonQuery(_db, "UPDATE purchases SET control = 3 WHERE journal_no = @j",
                SqlHelper.Param("@j", deleted));
            SqlHelper.ExecuteNonQuery(_db, "UPDATE products SET cost_price = 0 WHERE product_code = 'P001'");

            new InventoryService(_db).CalculateAverageCost("P001").Should().Be(310000);
        }

        [Test]
        public void CalculateAverageCost_Fallback_SameDateTiebreakLatestPurchase()
        {
            for (int i = 0; i < 3; i++)
            {
                _service.CreatePurchaseInvoice(
                    new Purchase { SubCode = "V001", DueDate = "2026-05-04", DocDate = "2026-03-10" },
                    new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 1, UnitPrice = 300000 + i * 10000 } },
                    1);
            }
            SqlHelper.ExecuteNonQuery(_db, "UPDATE products SET cost_price = 0 WHERE product_code = 'P001'");

            new InventoryService(_db).CalculateAverageCost("P001").Should().Be(320000);
        }

        [Test]
        public void CreatePurchaseReturn_WithInvoice_OffsetsAP()
        {
            // Create invoice first
            string invoiceJnl = _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } },
                1);

            // Return 3 units with invoice reference
            var ret = new Purchase { SubCode = "V001", RefNo = invoiceJnl };
            var retItems = new List<PurchaseItem>
            {
                new PurchaseItem { ProductCode = "P001", Quantity = 3, UnitPrice = 300000 }
            };

            _service.CreatePurchaseReturn(ret, retItems, true, 1);

            // AP should be partially paid
            var ap = _payablesRepo.GetByJournalNo(invoiceJnl);
            ap.PaymentAmount.Should().Be(900000); // 3 × 300000
        }

        [Test]
        public void FullChain_PO_GR_Invoice_Return()
        {
            // 1. Create PO
            string poJnl = _service.CreatePurchaseOrder(
                new Order { SubCode = "V001" },
                new List<OrderItem> { new OrderItem { ProductCode = "P001", Quantity = 20, UnitPrice = 300000 } },
                1);

            // 2. Receive goods against the PO
            string grJnl = _service.CreateGoodsReceipt(
                new Purchase { SubCode = "V001", RefNo = poJnl },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 20, UnitPrice = 300000, OrderRef = poJnl } },
                1);

            // 3. Create invoice billing the receipt (lines linked to the BPB, so no second stock-in)
            string invJnl = _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", RefNo = grJnl },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 20, UnitPrice = 300000, OrderRef = grJnl } },
                1);

            // 4. Return 5 units
            _service.CreatePurchaseReturn(
                new Purchase { SubCode = "V001", RefNo = invJnl },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 5, UnitPrice = 300000 } },
                true, 1);

            // Verify final state
            _movementRepo.GetStockOnHand("P001").Should().Be(15); // 20 received - 5 returned
            var ap = _payablesRepo.GetByJournalNo(invJnl);
            ap.PaymentAmount.Should().Be(1500000); // 5 × 300000 offset
        }

        // F19: the invoice + its AP entry must be atomic. Force the AP insert to fail
        // (sub_code is NOT NULL) AFTER the purchase row is written, and assert nothing
        // partial survives.
        [Test]
        public void CreatePurchaseInvoice_APFailure_RollsBackPurchase()
        {
            var invoice = new Purchase { SubCode = null, DueDate = "2026-05-04" }; // null sub_code fails AP insert
            var items = new List<PurchaseItem>
            {
                new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 }
            };

            System.Action act = () => _service.CreatePurchaseInvoice(invoice, items, 1);
            act.Should().Throw<Microsoft.Data.Sqlite.SqliteException>();

            CountRows("purchases").Should().Be(0, "the purchase row must roll back when the AP entry fails");
            CountRows("purchase_items").Should().Be(0, "the purchase items must roll back too");
            CountRows("payables_register").Should().Be(0, "no partial AP entry");
        }

        private int CountRows(string table)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM " + table;
            return System.Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}
