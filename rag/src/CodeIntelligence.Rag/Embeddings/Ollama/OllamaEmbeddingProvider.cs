using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Embeddings.Ollama;

/// <summary>
/// <see cref="IEmbeddingProvider"/> backed by a local Ollama instance
/// (https://github.com/ollama/ollama/blob/main/docs/api.md#generate-embeddings), typically
/// running nomic-embed-text. No API key and no external rate limits — the trade-off this
/// provider exists for is a hosted account (Voyage) with a strict free-tier rate limit
/// (3 RPM without a payment method) versus local compute with no such ceiling.
///
/// Uses the batch-capable POST /api/embed endpoint (not the older single-text
/// /api/embeddings) so a whole file's chunks still go out as one request, matching
/// VoyageEmbeddingProvider's batching behavior.
/// </summary>
public sealed class OllamaEmbeddingProvider(HttpClient httpClient, IOptions<OllamaOptions> options) : IEmbeddingProvider
{
    private readonly OllamaOptions _options = options.Value;

    public string ModelName => _options.Model;
    public int Dimension => _options.Dimension;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var results = new float[texts.Count][];

        for (var offset = 0; offset < texts.Count; offset += _options.BatchSize)
        {
            var batch = texts.Skip(offset).Take(_options.BatchSize).ToList();
            var embeddings = await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false);

            for (var i = 0; i < embeddings.Count; i++)
            {
                results[offset + i] = embeddings[i];
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> batch, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.RequestTimeout);

        try
        {
            var request = new OllamaEmbedRequest { Model = _options.Model, Input = batch };
            var response = await httpClient.PostAsJsonAsync("api/embed", request, timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new EmbeddingProviderException(
                    $"Ollama embedding request failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<OllamaEmbedResponse>(timeoutCts.Token)
                .ConfigureAwait(false);

            if (payload is null || payload.Embeddings.Count != batch.Count)
            {
                throw new EmbeddingProviderException(
                    $"Ollama returned {payload?.Embeddings.Count ?? 0} embeddings for a batch of {batch.Count}.");
            }

            return payload.Embeddings.Select(e => e.ToArray()).ToList();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Most often the model still loading into memory on a cold Ollama instance,
            // not a permanent failure — worth a distinct message rather than a bare timeout.
            throw new EmbeddingProviderException(
                $"Ollama embedding request timed out after {_options.RequestTimeout}. " +
                "The model may still be loading into memory on its first call — try again.");
        }
        catch (HttpRequestException ex)
        {
            throw new EmbeddingProviderException(
                $"Could not reach Ollama at {httpClient.BaseAddress}. Is it running (`ollama serve`) " +
                $"and is the model pulled (`ollama pull {_options.Model}`)?", ex);
        }
    }
}
