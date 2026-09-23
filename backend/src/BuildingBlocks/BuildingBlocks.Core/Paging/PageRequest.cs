namespace BuildingBlocks.Core.Paging;

/// <summary>
/// Common paging/sorting inputs for list endpoints.
///
/// PageSize is clamped rather than validated away: a client asking for 10,000 rows
/// gets 100, not an error. Refusing the request would be technically correct and
/// operationally annoying; clamping protects the database either way.
/// </summary>
public record PageRequest
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Field name to sort by. Services whitelist the allowed values.</summary>
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; }

    public int Skip => (Page - 1) * PageSize;
}
