using System.Text.RegularExpressions;
using CodeIntelligence.Rag.Models;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Reranking;

/// <summary>
/// Deterministic reranker: no external model call, so it has zero added latency/cost and is
/// fully unit-testable. It blends the candidate's retrieval score with a lexical-overlap
/// signal (fraction of query tokens present in the chunk, plus a bonus for a verbatim
/// substring match), which tends to promote chunks that mention the query's exact
/// identifiers/terms above chunks that only matched on vague semantic similarity.
/// </summary>
public sealed partial class SimpleLexicalReranker(IOptions<RerankerOptions> options) : IReranker
{
    private readonly RerankerOptions _options = options.Value;

    public Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default)
    {
        var queryTokens = Tokenize(query);

        var reranked = candidates
            .Select(c => (Result: c, Score: ComputeScore(c, query, queryTokens)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Result with { Score = x.Score })
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(reranked);
    }

    private double ComputeScore(SearchResult candidate, string query, IReadOnlySet<string> queryTokens)
    {
        var overlap = queryTokens.Count == 0
            ? 0.0
            : (double)queryTokens.Count(t => candidate.Content.Contains(t, StringComparison.OrdinalIgnoreCase)) / queryTokens.Count;

        var exactMatchBonus = candidate.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
            ? _options.ExactMatchBonus
            : 0.0;

        return candidate.Score * _options.RetrievalScoreWeight
             + overlap * _options.TokenOverlapWeight
             + exactMatchBonus;
    }

    private static HashSet<string> Tokenize(string text) =>
        TokenPattern().Matches(text).Select(m => m.Value).Where(t => t.Length > 1).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"[A-Za-z0-9_]+")]
    private static partial Regex TokenPattern();
}
