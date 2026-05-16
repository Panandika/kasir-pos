using System.Collections.Generic;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Help.KnowledgeBase;
using Kasir.Models;

namespace Kasir.Tests.Help
{
    [TestFixture]
    public class HybridRetrieverTests
    {
        private static HelpFaqHit Hit(string anchor, string title)
        {
            return new HelpFaqHit
            {
                Title = title,
                Content = title + " body",
                DocPath = "faq.md",
                Anchor = anchor,
                Score = 0
            };
        }

        [Test]
        public void Fuse_MergesByDocPathAndAnchor()
        {
            var local = new List<HelpFaqHit> { Hit("a", "A"), Hit("b", "B") };
            var remote = new List<HelpFaqHit> { Hit("a", "A"), Hit("c", "C") };

            var fused = HybridRetriever.Fuse(local, remote);

            fused.Should().HaveCount(3);
            // 'a' appears in both lists at rank 1 → highest combined RRF
            fused[0].Anchor.Should().Be("a");
        }

        [Test]
        public void Fuse_RanksByCombinedRrfScore()
        {
            var local = new List<HelpFaqHit> { Hit("a", "A"), Hit("b", "B"), Hit("c", "C") };
            var remote = new List<HelpFaqHit> { Hit("c", "C"), Hit("a", "A") };

            var fused = HybridRetriever.Fuse(local, remote);

            // 'a' = local rank 1 + remote rank 2  → 1/61 + 1/62
            // 'c' = local rank 3 + remote rank 1  → 1/63 + 1/61
            // a should still beat c (rank 1 in either dominates).
            fused[0].Anchor.Should().Be("a");
        }

        [Test]
        public void Fuse_CapsAtFiveResults()
        {
            var local = new List<HelpFaqHit>();
            for (int i = 0; i < 8; i++) local.Add(Hit("a" + i, "A" + i));

            var fused = HybridRetriever.Fuse(local, new List<HelpFaqHit>());
            fused.Should().HaveCountLessOrEqualTo(5);
        }

        [Test]
        public void Fuse_EmptyInputs_ReturnsEmpty()
        {
            HybridRetriever.Fuse(new List<HelpFaqHit>(), new List<HelpFaqHit>()).Should().BeEmpty();
        }
    }
}
