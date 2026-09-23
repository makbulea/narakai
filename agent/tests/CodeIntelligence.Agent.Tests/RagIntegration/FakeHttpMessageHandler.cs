namespace CodeIntelligence.Agent.Tests.RagIntegration;

/// <summary>
/// Test double for the transport an HttpClient sends over — same shape as
/// rag/tests/CodeIntelligence.Rag.Tests/Embeddings/FakeHttpMessageHandler.cs. Each call
/// to SendAsync is handed to the supplied handler function so tests can script exact
/// responses without a real network call.
/// </summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        await handler(request).ConfigureAwait(false);
}
