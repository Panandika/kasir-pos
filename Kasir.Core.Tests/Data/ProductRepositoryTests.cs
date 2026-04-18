using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class ProductRepositoryTests
    {
        private SqliteConnection _db;
        private ProductRepository _repo;
        private ProductBarcodeRepository _barcodeRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new ProductRepository(_db);
            _barcodeRepo = new ProductBarcodeRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private Product CreateTestProduct(string code, string name, int price)
        {
            return new Product
            {
                ProductCode = code,
                Name = name,
                Price = price,
                Status = "A",
                Unit = "PCS",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            };
        }

        [Test]
        public void Insert_ValidProduct_ReturnsId()
        {
            var product = CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000);
            int id = _repo.Insert(product);
            id.Should().BeGreaterThan(0);
        }

        [Test]
        public void GetByCode_ExistingProduct_ReturnsProduct()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));

            var product = _repo.GetByCode("P001");

            product.Should().NotBeNull();
            product.ProductCode.Should().Be("P001");
            product.Name.Should().Be("MINYAK GORENG 2L");
            product.Price.Should().Be(3200000);
        }

        [Test]
        public void GetByCode_NonExistent_ReturnsNull()
        {
            _repo.GetByCode("XXXX").Should().BeNull();
        }

        [Test]
        public void GetByBarcode_ExistingBarcode_ReturnsProduct()
        {
            var product = CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000);
            product.Barcode = "8991234567890";
            _repo.Insert(product);

            var found = _repo.GetByBarcode("8991234567890");

            found.Should().NotBeNull();
            found.ProductCode.Should().Be("P001");
        }

        [Test]
        public void Update_ChangesPrice()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));
            var product = _repo.GetByCode("P001");

            product.Price = 3500000;
            _repo.Update(product);

            var updated = _repo.GetByCode("P001");
            updated.Price.Should().Be(3500000);
        }

        [Test]
        public void Deactivate_SetsStatusToI()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));
            var product = _repo.GetByCode("P001");

            _repo.Deactivate(product.Id, 1);

            var deactivated = _repo.GetByCode("P001");
            deactivated.Status.Should().Be("I");
        }

        [Test]
        public void GetAll_ReturnsActiveProducts()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG", 3200000));
            _repo.Insert(CreateTestProduct("P002", "SABUN CUCI", 1550000));
            _repo.Insert(CreateTestProduct("P003", "GULA PASIR", 1800000));

            var products = _repo.GetAll(10, 0);
            products.Count.Should().Be(3);
        }

        [Test]
        public void GetAll_ExcludesInactiveProducts()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG", 3200000));
            _repo.Insert(CreateTestProduct("P002", "SABUN CUCI", 1550000));
            var p2 = _repo.GetByCode("P002");
            _repo.Deactivate(p2.Id, 1);

            var products = _repo.GetAll(10, 0);
            products.Count.Should().Be(1);
        }

        [Test]
        public void SearchByText_ByName_ReturnsMatches()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));
            _repo.Insert(CreateTestProduct("P002", "SABUN CUCI 800G", 1550000));
            _repo.Insert(CreateTestProduct("P003", "MINYAK TAWON FF", 2500000));

            var results = _repo.SearchByText("MINYAK", 20);

            results.Count.Should().Be(2);
        }

        [Test]
        public void SearchByText_ByCode_ReturnsExactMatch()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));
            _repo.Insert(CreateTestProduct("P002", "SABUN CUCI 800G", 1550000));

            var results = _repo.SearchByText("P001", 20);

            results.Count.Should().Be(1);
            results[0].ProductCode.Should().Be("P001");
        }

        [Test]
        public void SearchByText_ShortQuery_ReturnsEmpty()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));

            var results = _repo.SearchByText("M", 20);

            results.Count.Should().Be(0);
        }

        [Test]
        public void SearchByText_EmptyQuery_ReturnsEmpty()
        {
            _repo.SearchByText("", 20).Count.Should().Be(0);
            _repo.SearchByText(null, 20).Count.Should().Be(0);
        }

        [Test]
        public void Insert_DuplicateCode_Throws()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG", 3200000));

            System.Action act = () =>
                _repo.Insert(CreateTestProduct("P001", "DUPLICATE", 1000000));

            act.Should().Throw<SqliteException>();
        }

        // ProductBarcode tests

        [Test]
        public void Barcode_InsertAndGetByBarcode()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));

            var barcode = new ProductBarcode
            {
                ProductCode = "P001",
                Barcode = "8991234567890",
                ProductName = "MINYAK GORENG 2L",
                QtyPerScan = 1,
                PriceOverride = 0
            };
            _barcodeRepo.Insert(barcode);

            var found = _barcodeRepo.GetByBarcode("8991234567890");
            found.Should().NotBeNull();
            found.ProductCode.Should().Be("P001");
            found.QtyPerScan.Should().Be(1);
        }

        [Test]
        public void Barcode_WithQtyPerScan_ReturnsCorrectQty()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG KARTON", 3200000));

            var barcode = new ProductBarcode
            {
                ProductCode = "P001",
                Barcode = "BOX001",
                ProductName = "MINYAK GORENG KRT",
                QtyPerScan = 12,
                PriceOverride = 0
            };
            _barcodeRepo.Insert(barcode);

            var found = _barcodeRepo.GetByBarcode("BOX001");
            found.QtyPerScan.Should().Be(12);
        }

        [Test]
        public void Barcode_WithPriceOverride_ReturnsOverridePrice()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));

            var barcode = new ProductBarcode
            {
                ProductCode = "P001",
                Barcode = "PROMO001",
                ProductName = "MINYAK GORENG PROMO",
                QtyPerScan = 1,
                PriceOverride = 2900000
            };
            _barcodeRepo.Insert(barcode);

            var found = _barcodeRepo.GetByBarcode("PROMO001");
            found.PriceOverride.Should().Be(2900000);
        }

        [Test]
        public void Barcode_GetByProductCode_ReturnsAllBarcodes()
        {
            _repo.Insert(CreateTestProduct("P001", "MINYAK GORENG 2L", 3200000));
            _barcodeRepo.Insert(new ProductBarcode { ProductCode = "P001", Barcode = "BC1", QtyPerScan = 1 });
            _barcodeRepo.Insert(new ProductBarcode { ProductCode = "P001", Barcode = "BC2", QtyPerScan = 12 });

            var barcodes = _barcodeRepo.GetByProductCode("P001");
            barcodes.Count.Should().Be(2);
        }
    }
}
