#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Help.KnowledgeBase
{
    public enum RetrievalConfidence { High, Ambiguous, None }

    public class RetrievalResult
    {
        public List<HelpFaqHit> Hits { get; set; } = new List<HelpFaqHit>();
        public RetrievalConfidence Confidence { get; set; }
        public bool DegradedToLocal { get; set; }
    }

    /// <summary>
    /// Reciprocal-Rank-Fusion of local FTS5 and remote pgvector results.
    /// score(d) = sum( 1 / (60 + rank_i(d)) ) over both rankings.
    ///
    /// Confidence:
    ///   - top1.score / (top1.score + top2.score) >= 0.7  →  High (Answer)
    ///   - between 0.4 and 0.7 with two distinct candidates →  Ambiguous (Disambiguate)
    ///   - otherwise → None (NoAnswer)
    ///
    /// Degrades to local FTS5 silently if remote returns empty (offline / AI down).
    /// </summary>
    public class HybridRetriever
    {
        private const int RrfK = 60;
        private const double HighThreshold = 0.7;
        private const double AmbiguousThreshold = 0.4;

        private readonly HelpFaqRepository _localRepo;
        private readonly IHelpAskClient? _remoteClient;

        public HybridRetriever(HelpFaqRepository localRepo, IHelpAskClient? remoteClient)
        {
            _localRepo = localRepo;
            _remoteClient = remoteClient;
        }

        public async Task<RetrievalResult> RetrieveAsync(
            string query, string registerId, CancellationToken ct)
        {
            var local = _localRepo.Search(query, 5);
            var remote = _remoteClient != null
                ? await _remoteClient.AskAsync(query, registerId, ct).ConfigureAwait(false)
                : new List<HelpFaqHit>();

            bool degraded = remote.Count == 0;
            var fused = Fuse(local, remote);

            return new RetrievalResult
            {
                Hits = fused,
                Confidence = ScoreToConfidence(fused),
                DegradedToLocal = degraded
            };
        }

        // Visible for tests. RRF over two ranked lists, dedup on (DocPath, Anchor).
        public static List<HelpFaqHit> Fuse(List<HelpFaqHit> local, List<HelpFaqHit> remote)
        {
            var bucket = new Dictionary<string, HelpFaqHit>();

            void Accumulate(List<HelpFaqHit> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var h = list[i];
                    string key = (h.DocPath ?? "") + "#" + (h.Anchor ?? "");
                    double rrf = 1.0 / (RrfK + (i + 1));
                    if (bucket.TryGetValue(key, out var existing))
                    {
                        existing.Score += rrf;
                    }
                    else
                    {
                        bucket[key] = new HelpFaqHit
                        {
                            Id = h.Id,
                            Title = h.Title,
                            Content = h.Content,
                            DocPath = h.DocPath,
                            Anchor = h.Anchor,
                            Score = rrf
                        };
                    }
                }
            }

            Accumulate(local);
            Accumulate(remote);

            var sorted = new List<HelpFaqHit>(bucket.Values);
            sorted.Sort((a, b) => b.Score.CompareTo(a.Score));
            // Cap at top 5
            if (sorted.Count > 5) sorted.RemoveRange(5, sorted.Count - 5);
            return sorted;
        }

        private static RetrievalConfidence ScoreToConfidence(List<HelpFaqHit> hits)
        {
            if (hits.Count == 0) return RetrievalConfidence.None;
            if (hits.Count == 1) return RetrievalConfidence.High;
            double share = hits[0].Score / (hits[0].Score + hits[1].Score);
            if (share >= HighThreshold) return RetrievalConfidence.High;
            if (share >= AmbiguousThreshold) return RetrievalConfidence.Ambiguous;
            return RetrievalConfidence.None;
        }
    }
}
