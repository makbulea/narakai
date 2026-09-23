using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Embeddings.Voyage;

/// <summary>
/// <see cref="IEmbeddingProvider"/> backed by the Voyage AI embeddings API
/// (https://docs.voyageai.com/reference/embeddings-api), defaulting to voyage-code-3 /
/// 1024 dimensions as required for this project.
///
/// Retry policy: transient failures (HTTP 429, HTTP 5xx, network errors, per-attempt
/// timeout) are retried up to <see cref="VoyageOptions.MaxRetries"/> times with exponential
/// backoff and full jitter, honoring a Retry-After header when the API sends one. Permanent
/// failures (4xx other than 429) are surfaced immediately without retrying, since retrying a
/// malformed request or an auth failure only wastes time and quota.
/// </summary>
public sealed class VoyageEmbeddingProvider(
    HttpClient httpClient,
    IOptions<VoyageOptions> options,
    ILogger<VoyageEmbeddingProvider> logger) : IEmbeddingProvider
{
    private readonly VoyageOptions _options = options.Value;

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
            var embeddings = await SendBatchWithRetryAsync(batch, inputType, cancellationToken).ConfigureAwait(false);

            for (var i = 0; i < embeddings.Count; i++)
            {
                results[offset + i] = embeddings[i];
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<float[]>> SendBatchWithRetryAsync(
        IReadOnlyList<string> batch, EmbeddingInputType inputType, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(_options.RequestTimeout);

            try
            {
                var response = await SendOnceAsync(batch, inputType, attemptCts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content
                        .ReadFromJsonAsync<VoyageEmbeddingResponse>(attemptCts.Token)
                        .ConfigureAwait(false);

                    if (payload is null || payload.Data.Count != batch.Count)
                    {
                        throw new EmbeddingProviderException(
                            $"Voyage returned {payload?.Data.Count ?? 0} embeddings for a batch of {batch.Count}.");
                    }

                    return payload.Data
                        .OrderBy(d => d.Index)
                        .Select(d => d.Embedding.ToArray())
                        .ToList();
                }

                if (!IsRetryable(response.StatusCode))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new EmbeddingProviderException(
                        $"Voyage embedding request failed permanently with {(int)response.StatusCode} {response.StatusCode}: {body}");
                }

                if (attempt >= _options.MaxRetries)
                {
                    throw new EmbeddingProviderException(
                        $"Voyage embedding request failed with {(int)response.StatusCode} after {attempt + 1} attempts.");
                }

                var retryAfter = response.Headers.RetryAfter?.Delta;
                await DelayBeforeRetryAsync(attempt, retryAfter, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Per-attempt timeout, not caller cancellation: treat like any other transient failure.
                if (attempt >= _options.MaxRetries)
                {
                    throw new EmbeddingProviderException(
                        $"Voyage embedding request timed out after {attempt + 1} attempts.");
                }

                logger.LogWarning("Voyage embedding request timed out on attempt {Attempt}; retrying", attempt + 1);
                await DelayBeforeRetryAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw new EmbeddingProviderException(
                        $"Voyage embedding request failed after {attempt + 1} attempts.", ex);
                }

                logger.LogWarning(ex, "Voyage embedding request failed on attempt {Attempt}; retrying", attempt + 1);
                await DelayBeforeRetryAsync(attempt, retryAfter: null, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task<HttpResponseMessage> SendOnceAsync(
        IReadOnlyList<string> batch, EmbeddingInputType inputType, CancellationToken cancellationToken)
    {
        var request = new VoyageEmbeddingRequest
        {
            Input = batch,
            Model = _options.Model,
            InputType = inputType == EmbeddingInputType.Query ? "query" : "document"
        };

        return httpClient.PostAsJsonAsync(_options.BaseUrl, request, cancellationToken);
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private async Task DelayBeforeRetryAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? ComputeBackoffWithJitter(attempt);
        logger.LogInformation("Retrying Voyage embedding request in {DelayMs} ms (attempt {Attempt})", delay.TotalMilliseconds, attempt + 1);
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan ComputeBackoffWithJitter(int attempt)
    {
        var exponential = _options.BaseRetryDelay * Math.Pow(2, attempt);
        var capped = exponential > _options.MaxRetryDelay ? _options.MaxRetryDelay : exponential;
        // Full jitter (AWS architecture blog: "Exponential Backoff And Jitter"): pick a random
        // delay in [0, capped] so retrying clients spread out instead of retrying in lockstep.
        var jittered = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped.TotalMilliseconds);
        return jittered;
    }
}
