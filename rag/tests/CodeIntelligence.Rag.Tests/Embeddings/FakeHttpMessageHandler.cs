namespace CodeIntelligence.Rag.Tests.Embeddings;

/// <summary>
/// Test double for the transport Voyage's HttpClient sends over. Each call to SendAsync is
/// handed to the supplied handler function so tests can script exact response sequences
/// (retryable failures followed by success, permanent failures, etc.) without a real network call.
/// </summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    private int _callCount;

    public int CallCount => _callCount;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var callIndex = Interlocked.Increment(ref _callCount) - 1;
        return await handler(request, callIndex).ConfigureAwait(false);
    }
}
