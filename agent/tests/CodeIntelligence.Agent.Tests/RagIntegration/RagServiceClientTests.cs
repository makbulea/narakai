using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Core.Errors;
using CodeIntelligence.Agent.RagIntegration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeIntelligence.Agent.Tests.RagIntegration;

public sealed class RagServiceClientTests
{
    private static RagServiceClient BuildClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("http://rag.local/") };
        return new RagServiceClient(httpClient, NullLogger<RagServiceClient>.Instance);
    }

    [Fact]
    public async Task HybridSearchAsync_deserializes_a_successful_response()
    {
        var repositoryId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var client = BuildClient(async request =>
        {
            Assert.Equal("search/hybrid", request.RequestUri!.AbsolutePath.TrimStart('/'));
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains(repositoryId.ToString(), body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new
                    {
                        chunkId,
                        filePath = "src/A.cs",
                        startLine = 1,
                        endLine = 5,
                        content = "class A {}",
                        score = 0.8,
                        // symbolKind is deliberately numeric here, not a string: RAG's API host has
                        // no JsonStringEnumConverter configured, so SymbolKind (a real enum on the
                        // RAG side) serializes as its underlying int — this fixture must match that
                        // wire format or the test would validate a wrong assumption instead of reality.
                        metadata = new
                        {
                            language = "csharp", service = (string?)null, @namespace = (string?)null,
                            className = "A", methodName = (string?)null, symbolKind = (int)RagSymbolKind.Class
                        }
                    }
                })
            };
        });

        var results = await client.HybridSearchAsync(repositoryId, "query", 5);

        Assert.Single(results);
        Assert.Equal("src/A.cs", results[0].FilePath);
        Assert.Equal(chunkId, results[0].ChunkId);
    }

    [Fact]
    public async Task GetRepositoryAsync_returns_null_on_404()
    {
        var client = BuildClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var repository = await client.GetRepositoryAsync(Guid.NewGuid());

        Assert.Null(repository);
    }

    [Fact]
    public async Task Transport_failure_becomes_a_DownstreamServiceException()
    {
        var client = BuildClient(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<DownstreamServiceException>(() => client.HybridSearchAsync(Guid.NewGuid(), "q", 5));
    }
}
