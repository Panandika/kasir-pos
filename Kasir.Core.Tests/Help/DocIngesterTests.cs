using NUnit.Framework;
using FluentAssertions;
using Kasir.Help.KnowledgeBase;

namespace Kasir.Tests.Help
{
    [TestFixture]
    public class DocIngesterTests
    {
        private DocIngester _ingester;

        [SetUp]
        public void SetUp()
        {
            _ingester = new DocIngester();
        }

        [Test]
        public void Parse_SplitsByH2Headings()
        {
            string md = @"# Doc Title

## Diskon

Pilih baris item lalu tekan F8.

## Void item

Tekan F4 untuk hapus baris.
";
            var chunks = _ingester.Parse(md, "faq.md");
            chunks.Should().HaveCount(2);
            chunks[0].Title.Should().Be("Diskon");
            chunks[0].Anchor.Should().Be("diskon");
            chunks[0].Content.Should().Contain("F8");
            chunks[1].Title.Should().Be("Void item");
            chunks[1].Anchor.Should().Be("void-item");
        }

        [Test]
        public void Parse_PrependsDocTitleToBody()
        {
            string md = @"# Bantuan Kasir

## Diskon

Tekan F8.
";
            var chunks = _ingester.Parse(md, "faq.md");
            chunks[0].Content.Should().StartWith("Bantuan Kasir");
        }

        [Test]
        public void Parse_FrontMatterTagsCarryToAllChunks()
        {
            string md = @"---
tags: [bantuan, faq]
---

## Diskon

Body 1.

## Void

Body 2.
";
            var chunks = _ingester.Parse(md, "faq.md");
            chunks.Should().HaveCount(2);
            chunks[0].Tags.Should().Be("bantuan,faq");
            chunks[1].Tags.Should().Be("bantuan,faq");
        }

        [Test]
        public void Parse_NoHeadings_OneChunk()
        {
            string md = "Just some plain text with no heading.";
            var chunks = _ingester.Parse(md, "plain.md");
            chunks.Should().HaveCount(1);
            chunks[0].Title.Should().Be("plain");
            chunks[0].Anchor.Should().BeNull();
        }

        [Test]
        public void Parse_H3InsideH2_StaysInsideChunk()
        {
            string md = @"## Section A

Intro.

### Sub A1

Sub body.

## Section B

Body B.
";
            var chunks = _ingester.Parse(md, "faq.md");
            chunks.Should().HaveCount(3);
            // Section A body should contain the H3 sub-heading reference
            chunks[0].Title.Should().Be("Section A");
            chunks[0].Content.Should().Contain("Sub A1");
            chunks[1].Title.Should().Be("Sub A1");
            chunks[2].Title.Should().Be("Section B");
        }

        [Test]
        public void Parse_EmptyBody_SkipsChunk()
        {
            string md = "## Empty\n\n## Real\n\nBody here.";
            var chunks = _ingester.Parse(md, "faq.md");
            chunks.Should().HaveCount(1);
            chunks[0].Title.Should().Be("Real");
        }

        [Test]
        public void Slugify_StripsPunctuation()
        {
            string md = "## Cara terapkan diskon!?\n\nBody.";
            var chunks = _ingester.Parse(md, "faq.md");
            chunks[0].Anchor.Should().Be("cara-terapkan-diskon");
        }
    }
}
