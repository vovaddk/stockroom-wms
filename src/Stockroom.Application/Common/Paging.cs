using Microsoft.EntityFrameworkCore;

namespace Stockroom.Application.Common;

/// <summary>Bound from the query string (<c>?page=2&amp;pageSize=50</c>). Out-of-range values are clamped, not rejected.</summary>
public sealed record PageQuery(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new([.. Items.Select(map)], Page, PageSize, TotalCount);
}

public static class PagingExtensions
{
    /// <remarks>The query must already be ordered, otherwise SQL Server paging is non-deterministic.</remarks>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page.SafePage - 1) * page.SafePageSize)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page.SafePage, page.SafePageSize, total);
    }
}
