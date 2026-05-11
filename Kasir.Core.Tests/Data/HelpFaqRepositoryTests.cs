using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class HelpFaqRepositoryTests
    {
        private SqliteConnection _db;
        private HelpFaqRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new HelpFaqRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private static HelpFaq Faq(string anchor, string title, string content, string tags = null)
        {
            return new HelpFaq
            {
                DocPath = "faq/diskon.md",
                Anchor = anchor,
                Title = title,
                Content = content,
                Tags = tags
            };
        }

        [Test]
        public void Insert_AddsRow_AndIndexesInFts()
        {
            _repo.Insert(Faq("apply-discount", "Cara terapkan diskon",
                "Pilih baris item lalu tekan F8 untuk siklus diskon 0/5/10 persen."));

            _repo.Count().Should().Be(1);
            var hits = _repo.Search("diskon");
            hits.Should().HaveCount(1);
            hits[0].Title.Should().Be("Cara terapkan diskon");
        }

        [Test]
        public void Search_RanksMostRelevantFirst()
        {
            _repo.Insert(Faq("a", "Diskon item",
                "F8 untuk diskon per item, kolom harga untuk manual"));
            _repo.Insert(Faq("b", "Void transaksi",
                "Tekan F4 untuk void baris terakhir"));
            _repo.Insert(Faq("c", "Reprint struk",
                "Menu cetak ulang dari riwayat penjualan"));

            var hits = _repo.Search("diskon");
            hits.Should().NotBeEmpty();
            hits[0].Title.Should().Be("Diskon item");
        }

        [Test]
        public void Search_EmptyQuery_ReturnsEmpty()
        {
            _repo.Insert(Faq("a", "X", "y"));
            _repo.Search("").Should().BeEmpty();
            _repo.Search("   ").Should().BeEmpty();
        }

        [Test]
        public void Search_StripsFtsMetacharacters()
        {
            _repo.Insert(Faq("a", "Diskon",
                "F8 untuk diskon per item"));

            // Bare `*` or `:` would otherwise crash FTS5 query parsing.
            System.Action act = () => _repo.Search("diskon* :");
            act.Should().NotThrow();
            _repo.Search("diskon* :").Should().HaveCount(1);
        }

        [Test]
        public void Search_RespectsLimit()
        {
            for (int i = 0; i < 8; i++)
            {
                _repo.Insert(Faq("a" + i, "Diskon " + i,
                    "F8 untuk diskon kasus " + i));
            }
            _repo.Search("diskon", 3).Should().HaveCount(3);
        }

        [Test]
        public void Upsert_ReplacesByDocPathAndAnchor()
        {
            _repo.Upsert(Faq("apply-discount", "Diskon v1", "isi v1"));
            _repo.Upsert(Faq("apply-discount", "Diskon v2", "isi v2"));

            _repo.Count().Should().Be(1);
            var hits = _repo.Search("v2");
            hits.Should().HaveCount(1);
            hits[0].Title.Should().Be("Diskon v2");
        }

        [Test]
        public void Clear_RemovesAllRows()
        {
            _repo.Insert(Faq("a", "X", "y"));
            _repo.Insert(Faq("b", "X", "y"));
            _repo.Clear();
            _repo.Count().Should().Be(0);
            _repo.Search("y").Should().BeEmpty();
        }
    }
}
