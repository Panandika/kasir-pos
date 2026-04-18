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

            // 2. Receive goods
            string grJnl = _service.CreateGoodsReceipt(
                new Purchase { SubCode = "V001", RefNo = poJnl },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 20, UnitPrice = 300000 } },
                1);

            // 3. Create invoice
            string invJnl = _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04", RefNo = grJnl },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 20, UnitPrice = 300000 } },
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
    }
}
