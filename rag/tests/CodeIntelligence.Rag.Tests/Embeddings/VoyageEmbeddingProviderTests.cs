using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Embeddings.Voyage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Tests.Embeddings;

public sealed class VoyageEmbeddingProviderTests
{
    private static VoyageOptions FastOptions(
        int batchSize = 128, int maxRetries = 2, int baseRetryDelayMs = 1, int maxRetryDelayMs = 5) => new()
    {
        ApiKey = "test-key",
        BatchSize = batchSize,
        MaxRetries = maxRetries,
        BaseRetryDelay = TimeSpan.FromMilliseconds(baseRetryDelayMs),
        MaxRetryDelay = TimeSpan.FromMilliseconds(maxRetryDelayMs),
        RequestTimeout = TimeSpan.FromSeconds(10)
    };

    private static VoyageEmbeddingProvider CreateProvider(
        FakeHttpMessageHandler handler, VoyageOptions options)
    {
        var httpClient = new HttpClient(handler);
        return new VoyageEmbeddingProvider(httpClient, Options.Create(options), NullLogger<VoyageEmbeddingProvider>.Instance);
    }

    private static async Task<VoyageEmbeddingRequest> ReadRequestAsync(HttpRequestMessage request) =>
        (await request.Content!.ReadFromJsonAsync<VoyageEmbeddingRequest>())!;

    private static HttpResponseMessage SuccessResponse(VoyageEmbeddingRequest requestBody)
    {
        var response = new VoyageEmbeddingResponse
        {
            Model = requestBody.Model,
            Data = requestBody.Input
                .Select((text, i) => new VoyageEmbeddingData { Index = i, Embedding = [ExtractOrdinal(text)] })
                .ToList()
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
    }

    /// <summary>Extracts the trailing integer out of texts shaped like "text-3" so a batched response can be verified against global input order.</summary>
    private static float ExtractOrdinal(string text) => float.Parse(text.Split('-')[^1]);

    [Fact]
    public async Task EmbedAsync_LargeInputList_BatchesRequestsAccordingToBatchSize()
    {
        var texts = Enumerable.Range(0, 5).Select(i => $"text-{i}").ToList();
        var seenBatchSizes = new List<int>();

        var handler = new FakeHttpMessageHandler(async (request, _) =>
        {
            var body = await ReadRequestAsync(request);
            seenBatchSizes.Add(body.Input.Count);
            return SuccessResponse(body);
        });

        var provider = CreateProvider(handler, FastOptions(batchSize: 2));

        var results = await provider.EmbedAsync(texts, EmbeddingInputType.Document);

        Assert.Equal(3, handler.CallCount); // ceil(5 / 2) = 3 requests
        Assert.Equal([2, 2, 1], seenBatchSizes);

        Assert.Equal(5, results.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal((float)i, results[i][0]);
        }
    }

    [Fact]
    public async Task EmbedAsync_EmptyInput_MakesNoHttpCalls()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var provider = CreateProvider(handler, FastOptions());

        var results = await provider.EmbedAsync([], EmbeddingInputType.Document);

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task EmbedAsync_SetsInputTypeFromEmbeddingInputType()
    {
        string? capturedInputType = null;
        var handler = new FakeHttpMessageHandler(async (request, _) =>
        {
            var body = await ReadRequestAsync(request);
            capturedInputType = body.InputType;
            return SuccessResponse(body);
        });

        var provider = CreateProvider(handler, FastOptions());
        await provider.EmbedAsync(["query-text-0"], EmbeddingInputType.Query);

        Assert.Equal("query", capturedInputType);
    }

    [Fact]
    public async Task EmbedAsync_RetryableStatus_RetriedUpToMaxRetriesThenThrows()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var provider = CreateProvider(handler, FastOptions(maxRetries: 2));

        var ex = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => provider.EmbedAsync(["a"], EmbeddingInputType.Document));

        // MaxRetries=2 retries on top of the initial attempt = 3 total calls.
        Assert.Equal(3, handler.CallCount);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_TooManyRequestsStatus_IsRetried()
    {
        var handler = new FakeHttpMessageHandler((_, callIndex) =>
            Task.FromResult(new HttpResponseMessage(callIndex == 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.InternalServerError)));

        var provider = CreateProvider(handler, FastOptions(maxRetries: 2));

        await Assert.ThrowsAsync<EmbeddingProviderException>(() => provider.EmbedAsync(["a"], EmbeddingInputType.Document));

        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task EmbedAsync_PermanentClientError_FailsImmediatelyWithoutRetrying()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("invalid request")
            }));

        var provider = CreateProvider(handler, FastOptions(maxRetries: 5));

        var ex = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => provider.EmbedAsync(["a"], EmbeddingInputType.Document));

        Assert.Equal(1, handler.CallCount);
        Assert.Contains("400", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_UnauthorizedStatus_FailsImmediatelyWithoutRetrying()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var provider = CreateProvider(handler, FastOptions(maxRetries: 5));

        await Assert.ThrowsAsync<EmbeddingProviderException>(() => provider.EmbedAsync(["a"], EmbeddingInputType.Document));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task EmbedAsync_TransientFailureThenSuccess_RecoversAndReturnsEmbeddings()
    {
        var handler = new FakeHttpMessageHandler(async (request, callIndex) =>
        {
            if (callIndex == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var body = await ReadRequestAsync(request);
            return SuccessResponse(body);
        });

        var provider = CreateProvider(handler, FastOptions(maxRetries: 3));

        var results = await provider.EmbedAsync(["text-0"], EmbeddingInputType.Document);

        Assert.Equal(2, handler.CallCount);
        Assert.Single(results);
        Assert.Equal(0f, results[0][0]);
    }

    [Fact]
    public async Task EmbedAsync_RetryAfterHeader_IsHonoredInsteadOfExponentialBackoff()
    {
        var handler = new FakeHttpMessageHandler(async (request, callIndex) =>
        {
            if (callIndex == 0)
            {
                var retryResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                retryResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(15));
                return retryResponse;
            }

            var body = await ReadRequestAsync(request);
            return SuccessResponse(body);
        });

        // Backoff params are deliberately huge (30s) so if the code fell back to jittered
        // exponential backoff instead of honoring Retry-After, this test would almost always
        // run for seconds and blow well past the 1s assertion below.
        var options = FastOptions(maxRetries: 3, baseRetryDelayMs: 30_000, maxRetryDelayMs: 30_000);

        var provider = CreateProvider(handler, options);
        var stopwatch = Stopwatch.StartNew();

        var results = await provider.EmbedAsync(["text-0"], EmbeddingInputType.Document);

        stopwatch.Stop();

        Assert.Equal(2, handler.CallCount);
        Assert.Single(results);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Expected the Retry-After header (15ms) to be honored, but the retry took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public async Task EmbedAsync_MismatchedResponseCount_ThrowsEmbeddingProviderException()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = new VoyageEmbeddingResponse { Data = [new VoyageEmbeddingData { Index = 0, Embedding = [1f] }] };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });
        });

        var provider = CreateProvider(handler, FastOptions());

        // Two texts requested, but the fake server only returns one embedding.
        await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => provider.EmbedAsync(["a", "b"], EmbeddingInputType.Document));
    }

    [Fact]
    public void ModelNameAndDimension_ReflectConfiguredOptions()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("not used"));
        var options = new VoyageOptions { ApiKey = "test-key", Model = "voyage-code-3", Dimension = 1024 };

        var provider = CreateProvider(handler, options);

        Assert.Equal("voyage-code-3", provider.ModelName);
        Assert.Equal(1024, provider.Dimension);
    }
}
