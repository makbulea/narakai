namespace CodeIntelligence.Rag.Embeddings.Voyage;

/// <summary>
/// Configuration for <see cref="VoyageEmbeddingProvider"/>. Bind from "Rag:Embeddings:Voyage".
/// <see cref="ApiKey"/> must come from configuration/environment (VOYAGE_API_KEY) — never
/// hard-code it. See DependencyInjection/ServiceCollectionExtensions for the binding.
/// </summary>
public sealed class VoyageOptions
{
    public const string SectionName = "Rag:Embeddings:Voyage";

    /// <summary>
    /// Not "required init" on purpose: DI wiring falls back to the VOYAGE_API_KEY environment
    /// variable via IOptions PostConfigure, which needs a settable property. Missing/empty is
    /// validated (and throws) at service registration time — see ServiceCollectionExtensions.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; init; } = "voyage-code-3";
    public int Dimension { get; init; } = 1024;

    /// <summary>Texts per HTTP request. Voyage bills and rate-limits per request, so batching matters.</summary>
    public int BatchSize { get; init; } = 128;

    public Uri BaseUrl { get; init; } = new("https://api.voyageai.com/v1/embeddings");

    public int MaxRetries { get; init; } = 5;
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
