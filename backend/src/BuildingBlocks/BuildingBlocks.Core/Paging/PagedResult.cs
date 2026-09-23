namespace BuildingBlocks.Core.Paging;

/// <summary>
/// One page of results plus enough metadata for a client to page through the rest.
/// Returned by every list endpoint so pagination looks the same across services.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}
