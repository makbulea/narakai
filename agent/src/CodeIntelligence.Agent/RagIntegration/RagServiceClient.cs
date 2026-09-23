using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Core.Errors;
using Microsoft.Extensions.Logging;

namespace CodeIntelligence.Agent.RagIntegration;

/// <summary>
/// HTTP client for CodeIntelligence.Rag.Api. Follows the exact shape backend's own
/// ServiceClients.cs uses: transport failures become <see cref="DownstreamServiceException"/>
/// (mapped to 502 by BuildingBlocks.Web's GlobalExceptionHandler in the Api host), a 404
/// is a legitimate null rather than an exception.
/// </summary>
public sealed class RagServiceClient(HttpClient http, ILogger<RagServiceClient> logger) : IRagServiceClient
{
    // ASP.NET Core's default JSON options use camelCase; JsonSerializerDefaults.Web
    // matches that so requests/responses line up with rag.Api's minimal-API endpoints
    // without needing [JsonPropertyName] on every DTO.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RagSearchResult>> HybridSearchAsync(
        Guid repositoryId, string query, int topK, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                "search/hybrid",
                new { RepositoryId = repositoryId, Query = query, TopK = topK },
                JsonOptions,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<RagSearchResult>>(JsonOptions, cancellationToken)
                   ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "RAG hybrid search failed for repository {RepositoryId}", repositoryId);
            throw new DownstreamServiceException("CodeIntelligence.Rag", "Could not reach the RAG service.", ex);
        }
    }

    public async Task<RagRepository?> GetRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await http.GetAsync($"repositories/{repositoryId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<RagRepository>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "RAG repository lookup failed for {RepositoryId}", repositoryId);
            throw new DownstreamServiceException("CodeIntelligence.Rag", "Could not reach the RAG service.", ex);
        }
    }
}
