using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    // PO → goods receipt → purchase invoice linking. Receipt lines carry the PO number
    // in order_ref; invoice lines carry the receipt (BPB) number in order_ref.
    [TestFixture]
    public class PurchaseMatchingTests
    {
        private SqliteConnection _db;
        private PurchasingService _service;
        private StockMovementRepository _movementRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new PurchasingService(_db, new FakeClock(new System.DateTime(2026, 4, 4, 10, 0, 0)));
            _movementRepo = new StockMovementRepository(_db);
            new ConfigRepository(_db).Set("register_id", "01");

            var productRepo = new ProductRepository(_db);
            foreach (var code in new[] { "P001", "P002", "P003" })
            {
                productRepo.Insert(new Product
                {
                    ProductCode = code, Name = "PRODUCT " + code, Price = 500000,
                    BuyingPrice = 300000, Status = "A", OpenPrice = "N",
                    VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
                });
            }

            var vendorRepo = new SubsidiaryRepository(_db);
            vendorRepo.Insert(new Subsidiary { SubCode = "V001", Name = "VENDOR 1", GroupCode = "1", Status = "A" });
            vendorRepo.Insert(new Subsidiary { SubCode = "V002", Name = "VENDOR 2", GroupCode = "1", Status = "A" });
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private string CreatePo(string vendor = "V001")
        {
            return _service.CreatePurchaseOrder(
                new Order { SubCode = vendor },
                new List<OrderItem>
                {
                    new OrderItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 },
                    new OrderItem { ProductCode = "P002", Quantity = 5, UnitPrice = 200000 }
                }, 1);
        }

        private string Receive(string po, params (string code, int qty, long price)[] lines)
        {
            return _service.CreateGoodsReceipt(
                new Purchase { SubCode = "V001" },
                lines.Select(l => new PurchaseItem
                {
                    ProductCode = l.code, Quantity = l.qty, UnitPrice = l.price, OrderRef = po
                }).ToList(), 1);
        }

        private static List<PurchaseItem> InvoiceLines(string receipt, params (string code, int qty, long price)[] lines)
        {
            return lines.Select(l => new PurchaseItem
            {
                ProductCode = l.code, Quantity = l.qty, UnitPrice = l.price, OrderRef = receipt
            }).ToList();
        }

        // ---------- Receipt against PO ----------

        [Test]
        public void NewPo_IsOpen_WithFullRemaining()
        {
            string po = CreatePo();

            _service.GetOrderStatus(po).Should().Be(PurchasingService.OrderStatusOpen);
            var lines = _service.GetOrderReceiptStatus(po);
            lines.Should().HaveCount(2);
            lines.Single(l => l.ProductCode == "P001").Remaining.Should().Be(10);
            lines.Single(l => l.ProductCode == "P002").Remaining.Should().Be(5);
        }

        [Test]
        public void PartialReceipt_MarksPoPartial_AndTracksRemaining()
        {
            string po = CreatePo();

            Receive(po, ("P001", 4, 300000));

            _service.GetOrderStatus(po).Should().Be(PurchasingService.OrderStatusPartial);
            var p1 = _service.GetOrderReceiptStatus(po).Single(l => l.ProductCode == "P001");
            p1.Received.Should().Be(4);
            p1.Remaining.Should().Be(6);
            _movementRepo.GetStockOnHand("P001").Should().Be(4);
        }

        [Test]
        public void FullReceipt_AcrossTwoReceipts_MarksPoDone_AndDropsFromOpenList()
        {
            string po = CreatePo();

            Receive(po, ("P001", 6, 300000), ("P002", 5, 200000));
            _service.GetOpenPurchaseOrders("V001").Select(o => o.JournalNo).Should().Contain(po);

            Receive(po, ("P001", 4, 300000));

            _service.GetOrderStatus(po).Should().Be(PurchasingService.OrderStatusDone);
            _service.GetOpenPurchaseOrders("V001").Select(o => o.JournalNo).Should().NotContain(po);
        }

        [Test]
        public void Receipt_StoresPoRefAndOrderedQty_OnLines()
        {
            string po = CreatePo();

            string gr = Receive(po, ("P001", 4, 300000));

            var line = new PurchaseRepository(_db).GetItems(gr).Single();
            line.OrderRef.Should().Be(po);
            line.QtyOrder.Should().Be(10);
        }

        [Test]
        public void OverReceipt_IsRejected_AndNothingIsSaved()
        {
            string po = CreatePo();
            Receive(po, ("P001", 8, 300000));

            System.Action act = () => Receive(po, ("P001", 3, 300000)); // 8 + 3 > 10

            act.Should().Throw<PurchaseValidationException>().WithMessage("*P001*");
            _movementRepo.GetStockOnHand("P001").Should().Be(8, "rejected receipt must not move stock");
            CountRows("purchases").Should().Be(1);
        }

        [Test]
        public void OverReceipt_SameProductSplitAcrossTwoLines_IsRejected()
        {
            string po = CreatePo();

            System.Action act = () => Receive(po, ("P001", 6, 300000), ("P001", 6, 300000));

            act.Should().Throw<PurchaseValidationException>();
        }

        [Test]
        public void Receipt_ProductNotOnPo_IsRejected()
        {
            string po = CreatePo();

            System.Action act = () => Receive(po, ("P003", 1, 300000));

            act.Should().Throw<PurchaseValidationException>().WithMessage("*P003*");
        }

        [Test]
        public void Receipt_AgainstOtherVendorsPo_IsRejected()
        {
            string po = CreatePo("V002");

            System.Action act = () => Receive(po, ("P001", 1, 300000)); // receipt vendor V001

            act.Should().Throw<PurchaseValidationException>();
        }

        [Test]
        public void Receipt_AgainstUnknownPo_IsRejected()
        {
            System.Action act = () => Receive("OMS-01-9999-0001", ("P001", 1, 300000));

            act.Should().Throw<PurchaseValidationException>();
        }

        [Test]
        public void Receipt_AgainstVoidedPo_IsRejected()
        {
            string po = CreatePo();
            Exec("UPDATE orders SET control = 3 WHERE journal_no = '" + po + "'");

            System.Action act = () => Receive(po, ("P001", 1, 300000));

            act.Should().Throw<PurchaseValidationException>();
        }

        [Test]
        public void VoidedReceipt_DoesNotCountAsReceived()
        {
            string po = CreatePo();
            string gr = Receive(po, ("P001", 10, 300000));
            Exec("UPDATE purchases SET control = 3 WHERE journal_no = '" + gr + "'");

            _service.GetOrderReceiptStatus(po).Single(l => l.ProductCode == "P001").Remaining.Should().Be(10);
        }

        [Test]
        public void Receipt_WithoutPo_StillWorks()
        {
            string gr = _service.CreateGoodsReceipt(
                new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P003", Quantity = 2, UnitPrice = 100 } }, 1);

            gr.Should().Contain("BPB");
            _movementRepo.GetStockOnHand("P003").Should().Be(2);
        }

        // ---------- Invoice against receipt ----------

        [Test]
        public void Invoice_MatchingReceipt_HasNoIssues_AndReceiptBecomesFullyInvoiced()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));
            var lines = InvoiceLines(gr, ("P001", 10, 300000));

            var match = _service.CheckInvoiceMatch("V001", lines);
            match.Errors.Should().BeEmpty();
            match.Warnings.Should().BeEmpty();

            _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" }, lines, 1);

            _service.GetReceiptInvoiceStatus(gr).Single().Uninvoiced.Should().Be(0);
            _service.GetUninvoicedReceipts("V001").Select(p => p.JournalNo).Should().NotContain(gr);
        }

        [Test]
        public void Invoice_PartialBilling_LeavesRemainderUninvoiced()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));

            _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 7, 300000)), 1);

            _service.GetReceiptInvoiceStatus(gr).Single().Uninvoiced.Should().Be(3);
            _service.GetUninvoicedReceipts("V001").Select(p => p.JournalNo).Should().Contain(gr);
        }

        [Test]
        public void Invoice_BillingMoreThanReceived_IsBlocked()
        {
            string gr = Receive(CreatePo(), ("P001", 9, 300000));
            var lines = InvoiceLines(gr, ("P001", 10, 300000));

            _service.CheckInvoiceMatch("V001", lines).Errors.Should().ContainSingle(e => e.Contains("P001"));

            System.Action act = () => _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" }, lines, 1);
            act.Should().Throw<PurchaseValidationException>();
            CountRows("payables_register").Should().Be(0, "blocked invoice must not create AP");
        }

        [Test]
        public void Invoice_SecondBillForSameReceipt_OnlyUninvoicedQtyAllowed()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));
            _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 10, 300000)), 1);

            var dupe = InvoiceLines(gr, ("P001", 1, 300000));

            _service.CheckInvoiceMatch("V001", dupe).Errors.Should().NotBeEmpty("receipt already fully billed");
        }

        [Test]
        public void Invoice_ProductNotOnReceipt_IsBlocked()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));

            _service.CheckInvoiceMatch("V001", InvoiceLines(gr, ("P002", 1, 200000)))
                .Errors.Should().ContainSingle(e => e.Contains("P002"));
        }

        [Test]
        public void Invoice_OtherVendorsReceipt_IsBlocked()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));

            _service.CheckInvoiceMatch("V002", InvoiceLines(gr, ("P001", 10, 300000)))
                .Errors.Should().NotBeEmpty();
        }

        [Test]
        public void Invoice_UnknownReceipt_IsBlocked()
        {
            _service.CheckInvoiceMatch("V001", InvoiceLines("BPB-01-9999-0001", ("P001", 1, 300000)))
                .Errors.Should().NotBeEmpty();
        }

        [Test]
        public void Invoice_PriceDifferentFromReceipt_IsWarningNotError_AndCanBeSaved()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));
            var lines = InvoiceLines(gr, ("P001", 10, 310000));

            var match = _service.CheckInvoiceMatch("V001", lines);
            match.Errors.Should().BeEmpty();
            match.Warnings.Should().ContainSingle(w => w.Contains("P001"));

            string inv = _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" }, lines, 1);
            new PayablesRepository(_db).GetByJournalNo(inv).Amount.Should().Be(3100000);
        }

        // ---------- Invoice stock behaviour (legacy MSK = receive + bill in one) ----------

        [Test]
        public void Invoice_WithoutLink_IsCombinedReceiveAndBill_AddsStock()
        {
            var lines = new List<PurchaseItem> { new PurchaseItem { ProductCode = "P003", Quantity = 4, UnitPrice = 100 } };

            _service.CheckInvoiceMatch("V001", lines).Errors.Should().BeEmpty();
            _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" }, lines, 1)
                .Should().Contain("MSK");

            _movementRepo.GetStockOnHand("P003").Should().Be(4);
        }

        [Test]
        public void Invoice_LinkedToReceipt_DoesNotAddStockAgain()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));

            _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 10, 300000)), 1);

            _movementRepo.GetStockOnHand("P001").Should().Be(10, "goods were already received on the BPB");
        }

        [Test]
        public void Invoice_LinkedToPo_ReceivesAgainstPo_AndAddsStock()
        {
            string po = CreatePo();

            string inv = _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(po, ("P001", 10, 300000), ("P002", 5, 200000)), 1);

            _movementRepo.GetStockOnHand("P001").Should().Be(10);
            _service.GetOrderStatus(po).Should().Be(PurchasingService.OrderStatusDone);
            new PurchaseRepository(_db).GetItems(inv).First().QtyOrder.Should().Be(10);
        }

        [Test]
        public void Invoice_LinkedToPo_OverReceipt_IsBlocked()
        {
            string po = CreatePo();
            Receive(po, ("P001", 8, 300000));

            var lines = InvoiceLines(po, ("P001", 3, 300000));

            _service.CheckInvoiceMatch("V001", lines).Errors.Should().ContainSingle(e => e.Contains("P001"));
            System.Action act = () => _service.CreatePurchaseInvoice(
                new Purchase { SubCode = "V001", DueDate = "2026-05-04" }, lines, 1);
            act.Should().Throw<PurchaseValidationException>();
            _movementRepo.GetStockOnHand("P001").Should().Be(8);
        }

        [Test]
        public void VendorWithUnbilledReceipt_IsReported_ForUnlinkedInvoiceWarning()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));

            _service.GetUninvoicedReceipts("V001").Select(p => p.JournalNo).Should().Equal(gr);
            _service.GetUninvoicedReceipts("V002").Should().BeEmpty();
        }

        [Test]
        public void Invoice_StoresReceiptRef_OnLines()
        {
            string gr = Receive(CreatePo(), ("P001", 10, 300000));

            string inv = _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 10, 300000)), 1);

            new PurchaseRepository(_db).GetItems(inv).Single().OrderRef.Should().Be(gr);
        }

        [Test]
        public void GetLinkableDocType_ResolvesPoAndReceipt_AndRejectsOthers()
        {
            string po = CreatePo();
            string gr = Receive(po, ("P001", 1, 300000));
            string inv = _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P003", Quantity = 1, UnitPrice = 100 } }, 1);

            _service.GetLinkableDocType(po).Should().Be("PURCHASE_ORDER");
            _service.GetLinkableDocType(gr).Should().Be("RECEIPT");
            _service.GetLinkableDocType(inv).Should().BeNull();
            _service.GetLinkableDocType("NOPE").Should().BeNull();
        }

        [Test]
        public void RejectedReceipt_DoesNotConsumeDocumentNumber()
        {
            string po = CreatePo();
            string first = Receive(po, ("P001", 8, 300000));
            System.Action reject = () => Receive(po, ("P001", 5, 300000));
            reject.Should().Throw<PurchaseValidationException>();

            string next = Receive(po, ("P001", 2, 300000));

            Seq(next).Should().Be(Seq(first) + 1, "a rejected BPB must not leave a gap in the numbering");
        }

        [Test]
        public void RejectedInvoice_DoesNotConsumeDocumentNumber()
        {
            string gr = Receive(CreatePo(), ("P001", 5, 300000));
            string first = _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 2, 300000)), 1);
            System.Action reject = () => _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 9, 300000)), 1);
            reject.Should().Throw<PurchaseValidationException>();

            string next = _service.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-05-04" },
                InvoiceLines(gr, ("P001", 3, 300000)), 1);

            Seq(next).Should().Be(Seq(first) + 1);
        }

        [Test]
        public void PoWithoutItems_IsOpen_NotDone()
        {
            string po = _service.CreatePurchaseOrder(new Order { SubCode = "V001" }, new List<OrderItem>(), 1);

            _service.GetOrderStatus(po).Should().Be(PurchasingService.OrderStatusOpen);
        }

        [Test]
        public void OrderRefIndex_ExistsAfterMigrations()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_purchase_items_order_ref'";
            System.Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(1);
        }

        private static int Seq(string journalNo) => int.Parse(journalNo.Substring(journalNo.LastIndexOf('-') + 1));

        private int CountRows(string table)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM " + table;
            return System.Convert.ToInt32(cmd.ExecuteScalar());
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
